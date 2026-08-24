"use server";

import { revalidatePath } from "next/cache";
import { z } from "zod";
import { prisma } from "@/lib/db";
import {
  addToCart,
  getOrCreateCart,
  removeCartItem,
  setPromoCode,
  updateCartItem,
} from "@/lib/cart";
import { priceLines, splitCodes } from "@/lib/pricing";
import { getSession } from "@/lib/session";
import { toPrismaRx, prescriptionSchema } from "@/lib/rx";
import { audit } from "@/lib/audit";

const addSchema = z.object({
  variantId: z.string().min(1),
  qty: z.number().int().min(1).max(20).default(1),
  lensOptionCodes: z.array(z.string()).default([]),
  /** Rx typed during the lens builder, not yet attached to a patient file. */
  prescription: z.unknown().optional(),
  /** An existing verified prescription the customer picked from their file. */
  prescriptionId: z.string().optional(),
});

export type ActionResult = { ok: true; message?: string } | { ok: false; error: string };

export async function addToCartAction(input: unknown): Promise<ActionResult> {
  const parsed = addSchema.safeParse(input);
  if (!parsed.success) return { ok: false, error: "That selection wasn't understood." };
  const data = parsed.data;

  const variant = await prisma.frameVariant.findUnique({
    where: { id: data.variantId },
    include: { frame: true },
  });
  if (!variant || !variant.isActive || variant.frame.status !== "active") {
    return { ok: false, error: "That frame is no longer available." };
  }
  if (variant.stockQty <= 0) {
    return { ok: false, error: `${variant.frame.name} in ${variant.colorName} is out of stock.` };
  }

  // Validate the prescription here rather than trusting the client: this same
  // record is what the lab cuts lenses from.
  let rxDraft: unknown = undefined;
  if (data.prescription) {
    const rx = prescriptionSchema.safeParse(data.prescription);
    if (!rx.success) {
      const first = rx.error.issues[0];
      return { ok: false, error: first?.message ?? "Please check the prescription details." };
    }
    rxDraft = { ...toPrismaRx(rx.data), pdMm: rx.data.pdMm ?? null, pdNearMm: rx.data.pdNearMm ?? null };
  }

  // Reject lens combinations the lab can't make before they reach the bag.
  const [priced] = await priceLines([
    {
      variantId: data.variantId,
      qty: data.qty,
      lensOptionCodes: data.lensOptionCodes,
      rx: (rxDraft as never) ?? null,
    },
  ]);
  const blocking = priced?.warnings.filter((w) => !w.startsWith("Only ")) ?? [];
  if (blocking.length) return { ok: false, error: blocking[0] };

  const cart = await getOrCreateCart();
  await addToCart({
    cartId: cart.id,
    variantId: data.variantId,
    qty: data.qty,
    lensOptionCodes: data.lensOptionCodes,
    prescriptionDraft: rxDraft,
    prescriptionId: data.prescriptionId ?? null,
  });

  revalidatePath("/cart");
  revalidatePath("/", "layout");
  return { ok: true, message: "Added to your bag." };
}

export async function updateCartItemAction(itemId: string, qty: number): Promise<ActionResult> {
  const cart = await getOrCreateCart();
  const item = await prisma.cartItem.findUnique({ where: { id: itemId } });
  if (!item || item.cartId !== cart.id) return { ok: false, error: "Item not found." };

  await updateCartItem(itemId, qty);
  revalidatePath("/cart");
  revalidatePath("/", "layout");
  return { ok: true };
}

export async function removeCartItemAction(itemId: string): Promise<ActionResult> {
  const cart = await getOrCreateCart();
  const item = await prisma.cartItem.findUnique({ where: { id: itemId } });
  if (!item || item.cartId !== cart.id) return { ok: false, error: "Item not found." };

  await removeCartItem(itemId);
  revalidatePath("/cart");
  revalidatePath("/", "layout");
  return { ok: true };
}

export async function applyPromoAction(code: string): Promise<ActionResult> {
  const cart = await getOrCreateCart();
  await setPromoCode(cart.id, code || null);
  revalidatePath("/cart");
  revalidatePath("/checkout");

  const session = await getSession();
  await audit({
    userId: session?.userId,
    action: "promo.apply",
    entity: "Cart",
    entityId: cart.id,
    detail: { code },
  });
  return { ok: true };
}

/** Swap the lens build on a line without losing its place in the bag. */
export async function updateLensSelectionAction(
  itemId: string,
  codes: string[],
): Promise<ActionResult> {
  const cart = await getOrCreateCart();
  const item = await prisma.cartItem.findUnique({ where: { id: itemId } });
  if (!item || item.cartId !== cart.id) return { ok: false, error: "Item not found." };

  const sorted = [...new Set(codes)].sort();
  const [priced] = await priceLines([
    {
      variantId: item.variantId,
      qty: item.qty,
      lensOptionCodes: sorted,
      rx: item.prescriptionDraft ? JSON.parse(item.prescriptionDraft) : null,
    },
  ]);
  const blocking = priced?.warnings.filter((w) => !w.startsWith("Only ")) ?? [];
  if (blocking.length) return { ok: false, error: blocking[0] };

  await prisma.cartItem.update({
    where: { id: itemId },
    data: { lensOptionCodes: sorted.join(",") || null },
  });
  revalidatePath("/cart");
  return { ok: true };
}

export async function getCartLineCodes(itemId: string): Promise<string[]> {
  const item = await prisma.cartItem.findUnique({ where: { id: itemId } });
  return splitCodes(item?.lensOptionCodes);
}
