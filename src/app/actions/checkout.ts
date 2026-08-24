"use server";

import { redirect } from "next/navigation";
import { z } from "zod";
import { prisma } from "@/lib/db";
import { buildCartView, clearCartCookie, parseRxDraft, peekCartToken } from "@/lib/cart";
import { computeTotals, joinCodes, splitCodes } from "@/lib/pricing";
import { evaluatePromotions } from "@/lib/promotions";
import { quoteShipping } from "@/lib/shipping";
import { enabledPaymentMethods, startPayment } from "@/lib/payments";
import { getSession } from "@/lib/session";
import { ensurePatientForUser, nextFileNo } from "@/lib/auth";
import { summariseRx } from "@/lib/rx";
import { audit } from "@/lib/audit";
import { getSettingBool } from "@/lib/settings";
import { CURRENCY } from "@/lib/money";

const checkoutSchema = z.object({
  email: z.string().email("Enter a valid email address."),
  phone: z.string().min(6, "Enter a phone number we can reach you on.").max(30),
  fullName: z.string().min(2, "Enter the name for delivery.").max(120),
  line1: z.string().min(3, "Enter the street address.").max(200),
  line2: z.string().max(200).optional(),
  city: z.string().min(2, "Enter the city.").max(100),
  state: z.string().max(100).optional(),
  postalCode: z.string().max(20).optional(),
  country: z.string().length(2).default("PK"),
  shippingCode: z.string().optional(),
  paymentMethod: z.string().min(1, "Choose how you'd like to pay."),
  notes: z.string().max(1000).optional(),
  saveAddress: z.boolean().optional(),
});

export type CheckoutState = { error?: string; fieldErrors?: Record<string, string> };

export async function placeOrderAction(
  _prev: CheckoutState,
  formData: FormData,
): Promise<CheckoutState> {
  const raw = Object.fromEntries(formData) as Record<string, string>;
  const parsed = checkoutSchema.safeParse({
    ...raw,
    saveAddress: raw.saveAddress === "on",
  });

  if (!parsed.success) {
    const fieldErrors: Record<string, string> = {};
    for (const issue of parsed.error.issues) {
      const key = String(issue.path[0] ?? "form");
      fieldErrors[key] ??= issue.message;
    }
    return { error: "Please check the highlighted fields.", fieldErrors };
  }
  const input = parsed.data;

  if (!enabledPaymentMethods().some((m) => m.id === input.paymentMethod)) {
    return { error: "That payment method isn't available." };
  }

  const session = await getSession();
  if (!session && !(await getSettingBool("checkout.guestAllowed"))) {
    return { error: "Please sign in or create an account to complete your order." };
  }

  // --- Re-price everything from the database ------------------------------
  const token = await peekCartToken();
  const cart = token ? await prisma.cart.findUnique({ where: { token } }) : null;
  if (!cart) return { error: "Your bag has expired. Please add your frames again." };

  const view = await buildCartView(cart.id, { country: input.country });
  if (view.lines.length === 0) return { error: "Your bag is empty." };

  const blocking = view.warnings.filter((w) => w.includes("out of stock"));
  if (blocking.length) return { error: blocking[0] };

  // Some practices won't take an order they can't dispense. When that rule is
  // on, every line needs either a saved prescription or one typed at checkout.
  if (await getSettingBool("checkout.requirePrescription")) {
    const cartItems = await prisma.cartItem.findMany({ where: { cartId: cart.id } });
    const missing = cartItems.find((i) => !i.prescriptionId && !i.prescriptionDraft);
    if (missing) {
      return {
        error:
          "This store needs a prescription on every pair before checkout. Add yours to each item in your bag.",
      };
    }
  }

  const promo = await evaluatePromotions({
    lines: view.lines,
    code: cart.promoCode,
    userId: session?.userId ?? null,
    email: input.email,
  });

  const goods = view.lines.reduce((s, l) => s + l.totalMinor, 0);
  const quotes = await quoteShipping({
    subtotalMinor: goods - promo.discountMinor,
    country: input.country,
    state: input.state,
    postalCode: input.postalCode,
    itemCount: view.lines.reduce((s, l) => s + l.qty, 0),
    address: {
      fullName: input.fullName,
      line1: input.line1,
      line2: input.line2,
      city: input.city,
      state: input.state,
      postalCode: input.postalCode,
      country: input.country,
      phone: input.phone,
      email: input.email,
    },
  });
  const chosenQuote = quotes.find((q) => q.code === input.shippingCode) ?? quotes[0];
  const shippingMinor = promo.freeShipping ? 0 : (chosenQuote?.priceMinor ?? 0);

  const totals = computeTotals({
    lines: view.lines,
    discountMinor: promo.discountMinor,
    shippingMinor,
    currency: cart.currency || CURRENCY,
  });

  // --- Patient file -------------------------------------------------------
  const patient = session
    ? await ensurePatientForUser(session.userId)
    : await findOrCreateGuestPatient(input.email, input.phone, input.fullName);

  // --- Write the order ----------------------------------------------------
  const orderNo = await nextOrderNo();

  const order = await prisma.$transaction(async (tx) => {
    const address = await tx.address.create({
      data: {
        userId: session?.userId ?? null,
        fullName: input.fullName,
        phone: input.phone,
        line1: input.line1,
        line2: input.line2 || null,
        city: input.city,
        state: input.state || null,
        postalCode: input.postalCode || null,
        country: input.country,
        isDefault: Boolean(input.saveAddress),
      },
    });

    const created = await tx.order.create({
      data: {
        orderNo,
        userId: session?.userId ?? null,
        patientId: patient.id,
        email: input.email.toLowerCase(),
        phone: input.phone,
        status: "pending",
        paymentStatus: "unpaid",
        currency: totals.currency,
        subtotalMinor: totals.subtotalMinor,
        lensTotalMinor: totals.lensTotalMinor,
        discountMinor: totals.discountMinor,
        shippingMinor: totals.shippingMinor,
        taxMinor: totals.taxMinor,
        totalMinor: totals.totalMinor,
        promoCode: cart.promoCode,
        promotionId: promo.applied[0]?.id ?? null,
        shippingAddressId: address.id,
        billingAddressId: address.id,
        notes: input.notes || null,
      },
    });

    for (const line of view.lines) {
      const cartItem = await tx.cartItem.findUnique({ where: { id: line.itemId } });
      const draft = parseRxDraft(cartItem?.prescriptionDraft ?? null);

      // A prescription typed at checkout becomes a real, versioned record on
      // the patient's file — so a repeat order can reuse it and the optician
      // has something to verify.
      let prescriptionId = cartItem?.prescriptionId ?? null;
      if (!prescriptionId && draft) {
        // The binocular PD is a property of the person, not of one
        // prescription, so it is split out here and recorded on the file.
        const { pdMm, pdNearMm, ...eyeFields } = draft as typeof draft & {
          pdMm?: number | null;
          pdNearMm?: number | null;
        };

        const rx = await tx.prescription.create({
          data: {
            patientId: patient.id,
            source: "manual_entry",
            status: "pending_verification",
            ...eyeFields,
          },
        });
        prescriptionId = rx.id;

        if (pdMm) {
          await tx.patient.update({
            where: { id: patient.id },
            data: { pdMm, ...(pdNearMm ? { pdNearMm } : {}) },
          });
        }
      }

      const rxRecord = prescriptionId
        ? await tx.prescription.findUnique({ where: { id: prescriptionId } })
        : null;

      await tx.orderItem.create({
        data: {
          orderId: created.id,
          variantId: line.variantId,
          titleSnapshot: line.title,
          skuSnapshot: line.sku,
          imageSnapshot: line.imageUrl,
          qty: line.qty,
          unitPriceMinor: line.framePriceMinor,
          lensPriceMinor: line.lensPriceMinor,
          totalMinor: line.totalMinor,
          lensOptionCodes: joinCodes(line.lensOptions.map((o) => o.code)) || null,
          lensSummary: line.lensSummary,
          prescriptionId,
          // The snapshot must stand alone on an invoice or a remake years
          // later, so it carries the PD from the file alongside the Rx.
          prescriptionSnapshot: rxRecord
            ? JSON.stringify({
                ...rxRecord,
                summary: summariseRx(rxRecord),
                patientPdMm: patient.pdMm ?? (draft as { pdMm?: number })?.pdMm ?? null,
              })
            : null,
          labStatus: "pending",
        },
      });

      // Reserve stock at order time. Selling the last frame twice is far more
      // expensive to unpick than a brief oversell window is to avoid.
      await tx.frameVariant.update({
        where: { id: line.variantId },
        data: { stockQty: { decrement: line.qty } },
      });
    }

    // The cart is consumed; keep the row for analytics but empty the lines.
    await tx.cartItem.deleteMany({ where: { cartId: cart.id } });
    await tx.cart.update({ where: { id: cart.id }, data: { promoCode: null } });

    return created;
  });

  await audit({
    userId: session?.userId,
    action: "order.place",
    entity: "Order",
    entityId: order.id,
    detail: { orderNo, totalMinor: totals.totalMinor, method: input.paymentMethod },
  });

  // --- Payment ------------------------------------------------------------
  let redirectTo = `/order/${order.orderNo}`;
  try {
    const payment = await startPayment(order, input.paymentMethod);
    if (payment.redirectUrl) redirectTo = payment.redirectUrl;
  } catch (err) {
    console.error("[checkout] payment start failed", err);
    // The order exists and stock is held; staff can take payment manually
    // rather than losing the sale to a provider outage.
    await prisma.order.update({
      where: { id: order.id },
      data: {
        paymentStatus: "failed",
        internalNotes: `Payment could not be started: ${
          err instanceof Error ? err.message : String(err)
        }`,
      },
    });
  }

  if (chosenQuote) {
    await prisma.shipment.create({
      data: {
        orderId: order.id,
        carrier: chosenQuote.carrier,
        service: chosenQuote.name,
        costMinor: shippingMinor,
        status: "pending",
        providerRef: chosenQuote.rateRef ?? null,
      },
    });
  }

  await clearCartCookie();
  redirect(redirectTo);
}

/** Sequential, human-quotable order numbers: VC-2026-000123. */
async function nextOrderNo(): Promise<string> {
  const year = new Date().getFullYear();
  const prefix = `VC-${year}-`;
  const last = await prisma.order.findFirst({
    where: { orderNo: { startsWith: prefix } },
    orderBy: { orderNo: "desc" },
    select: { orderNo: true },
  });
  const n = last ? Number(last.orderNo.slice(prefix.length)) + 1 : 1;
  return `${prefix}${String(n).padStart(6, "0")}`;
}

/**
 * Guests still get a patient file — an optical order without one cannot be
 * dispensed, remade or followed up. Matching on email keeps a returning guest
 * on the same file rather than creating a new one each time.
 */
async function findOrCreateGuestPatient(email: string, phone: string, fullName: string) {
  const existing = await prisma.patient.findFirst({
    where: { email: email.toLowerCase(), deletedAt: null },
  });
  if (existing) return existing;

  const [firstName, ...rest] = fullName.trim().split(/\s+/);
  return prisma.patient.create({
    data: {
      fileNo: await nextFileNo(),
      firstName: firstName || "Guest",
      lastName: rest.join(" ") || "",
      email: email.toLowerCase(),
      phone,
    },
  });
}

/** Live delivery options for the checkout form, re-quoted as the address changes. */
export async function shippingQuotesAction(country: string, state?: string) {
  const token = await peekCartToken();
  const cart = token ? await prisma.cart.findUnique({ where: { token } }) : null;
  if (!cart) return [];

  const view = await buildCartView(cart.id, { country });
  const goods = view.lines.reduce((s, l) => s + l.totalMinor, 0);
  return quoteShipping({
    subtotalMinor: goods - view.totals.discountMinor,
    country,
    state,
    itemCount: view.itemCount,
  });
}

export async function cartLensCodes(itemId: string) {
  const item = await prisma.cartItem.findUnique({ where: { id: itemId } });
  return splitCodes(item?.lensOptionCodes);
}
