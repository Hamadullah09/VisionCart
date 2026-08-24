import "server-only";
import { prisma } from "./db";
import { CURRENCY } from "./money";
import type { Order } from "@prisma/client";

/**
 * Payment providers behind one interface.
 *
 *   cod           — cash on delivery, no integration needed
 *   bank_transfer — customer transfers manually, staff confirm in the back office
 *   stripe        — cards/wallets via PaymentIntents + webhook confirmation
 *
 * Which of these appear at checkout is driven by PAYMENT_PROVIDERS in .env, so
 * adding a card processor later is a config change plus one adapter below.
 */

export type PaymentMethodMeta = {
  id: string;
  label: string;
  description: string;
  /** Customer completes payment in the browser before the order is confirmed. */
  online: boolean;
};

const ALL_METHODS: Record<string, PaymentMethodMeta> = {
  cod: {
    id: "cod",
    label: "Cash on delivery",
    description: "Pay the courier when your glasses arrive.",
    online: false,
  },
  bank_transfer: {
    id: "bank_transfer",
    label: "Bank transfer",
    description: "Transfer to our account and send us the receipt.",
    online: false,
  },
  stripe: {
    id: "stripe",
    label: "Card payment",
    description: "Visa, Mastercard and wallets. Secured by Stripe.",
    online: true,
  },
};

export function enabledPaymentMethods(): PaymentMethodMeta[] {
  const configured = (process.env.PAYMENT_PROVIDERS || "cod")
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean);

  return configured
    .map((id) => ALL_METHODS[id])
    .filter((m): m is PaymentMethodMeta => Boolean(m))
    // Stripe without keys would render a dead card form — hide it instead.
    .filter((m) => m.id !== "stripe" || Boolean(process.env.STRIPE_SECRET_KEY));
}

export type StartPaymentResult = {
  paymentId: string;
  provider: string;
  /** Stripe: hand to the client SDK to confirm the card. */
  clientSecret?: string;
  /** Providers that take over the browser. */
  redirectUrl?: string;
  /** Offline methods: what the customer should do next. */
  instructions?: string;
  /** True when the order can be treated as placed without further action. */
  completed: boolean;
};

export async function startPayment(
  order: Order,
  method: string,
): Promise<StartPaymentResult> {
  const allowed = enabledPaymentMethods().map((m) => m.id);
  if (!allowed.includes(method)) {
    throw new Error(`Payment method "${method}" is not enabled for this store.`);
  }

  switch (method) {
    case "stripe":
      return startStripe(order);
    case "bank_transfer":
      return startBankTransfer(order);
    case "cod":
    default:
      return startCod(order);
  }
}

async function startCod(order: Order): Promise<StartPaymentResult> {
  const payment = await openOfflinePayment(order, "cod");
  return {
    paymentId: payment.id,
    provider: "cod",
    completed: true,
    instructions:
      "Have the exact amount ready for the courier. We'll call before delivery.",
  };
}

async function startBankTransfer(order: Order): Promise<StartPaymentResult> {
  const payment = await openOfflinePayment(order, "bank_transfer");
  return {
    paymentId: payment.id,
    provider: "bank_transfer",
    completed: true,
    instructions:
      process.env.BANK_TRANSFER_INSTRUCTIONS ||
      "Transfer the order total to our account and email the receipt quoting your order number.",
  };
}

/**
 * Hosted Stripe Checkout rather than an embedded card form: Stripe handles
 * SCA, wallets and PCI scope, and the shop never touches card data. The order
 * is confirmed by the webhook, not by the browser coming back — a customer who
 * closes the tab after paying still gets their glasses.
 */
async function startStripe(order: Order): Promise<StartPaymentResult> {
  const key = process.env.STRIPE_SECRET_KEY;
  if (!key) throw new Error("Stripe is selected but STRIPE_SECRET_KEY is not set.");

  const { default: Stripe } = await import("stripe");
  const stripe = new Stripe(key);
  const base = process.env.NEXT_PUBLIC_APP_URL || "http://localhost:3000";

  const session = await stripe.checkout.sessions.create({
    mode: "payment",
    customer_email: order.email || undefined,
    client_reference_id: order.id,
    // Charged as one line: the order total is already computed server-side
    // with lenses, discounts, delivery and tax settled.
    line_items: [
      {
        quantity: 1,
        price_data: {
          currency: (order.currency || CURRENCY).toLowerCase(),
          unit_amount: order.totalMinor,
          product_data: { name: `Order ${order.orderNo}` },
        },
      },
    ],
    metadata: { orderId: order.id, orderNo: order.orderNo },
    payment_intent_data: { metadata: { orderId: order.id, orderNo: order.orderNo } },
    success_url: `${base}/order/${order.orderNo}?paid=1`,
    cancel_url: `${base}/checkout?cancelled=${order.orderNo}`,
  });

  const payment = await prisma.payment.create({
    data: {
      orderId: order.id,
      provider: "stripe",
      status: "pending",
      amountMinor: order.totalMinor,
      currency: order.currency,
      providerRef: session.id,
    },
  });

  return {
    paymentId: payment.id,
    provider: "stripe",
    redirectUrl: session.url ?? undefined,
    completed: false,
  };
}

/**
 * Reuse the open payment row for offline methods so a customer who returns to
 * the confirmation page doesn't leave a trail of duplicate pending payments.
 */
async function openOfflinePayment(order: Order, provider: "cod" | "bank_transfer") {
  const existing = await prisma.payment.findFirst({
    where: { orderId: order.id, provider, status: "pending" },
    orderBy: { createdAt: "desc" },
  });

  if (existing) {
    return prisma.payment.update({
      where: { id: existing.id },
      data: { amountMinor: order.totalMinor, currency: order.currency },
    });
  }

  return prisma.payment.create({
    data: {
      orderId: order.id,
      provider,
      status: "pending",
      amountMinor: order.totalMinor,
      currency: order.currency,
    },
  });
}

/**
 * Mark an order paid. Called by the Stripe webhook and by staff confirming a
 * bank transfer, so the state transition lives in exactly one place.
 */
export async function markOrderPaid(args: {
  orderId: string;
  paymentId?: string;
  providerRef?: string;
  rawPayload?: unknown;
}) {
  await prisma.$transaction(async (tx) => {
    if (args.paymentId || args.providerRef) {
      await tx.payment.updateMany({
        where: args.paymentId
          ? { id: args.paymentId }
          : { orderId: args.orderId, providerRef: args.providerRef },
        data: {
          status: "succeeded",
          rawPayload: args.rawPayload ? JSON.stringify(args.rawPayload) : undefined,
        },
      });
    }

    const order = await tx.order.findUnique({ where: { id: args.orderId } });
    if (!order || order.paymentStatus === "paid") return; // webhooks retry; stay idempotent

    await tx.order.update({
      where: { id: args.orderId },
      data: {
        paymentStatus: "paid",
        status: order.status === "pending" ? "paid" : order.status,
        paidAt: new Date(),
      },
    });

    if (order.promotionId) {
      await tx.promotion.update({
        where: { id: order.promotionId },
        data: { usageCount: { increment: 1 } },
      });
    }
  });
}

export async function markPaymentFailed(args: {
  orderId: string;
  providerRef?: string;
  error?: string;
  rawPayload?: unknown;
}) {
  await prisma.payment.updateMany({
    where: { orderId: args.orderId, ...(args.providerRef ? { providerRef: args.providerRef } : {}) },
    data: {
      status: "failed",
      error: args.error ?? null,
      rawPayload: args.rawPayload ? JSON.stringify(args.rawPayload) : undefined,
    },
  });
  await prisma.order.update({
    where: { id: args.orderId },
    data: { paymentStatus: "failed" },
  });
}

/** Refund through the original provider where possible, else record it. */
export async function refundPayment(paymentId: string, amountMinor?: number) {
  const payment = await prisma.payment.findUnique({
    where: { id: paymentId },
    include: { order: true },
  });
  if (!payment) throw new Error("Payment not found");

  if (payment.provider === "stripe" && payment.providerRef && process.env.STRIPE_SECRET_KEY) {
    const { default: Stripe } = await import("stripe");
    const stripe = new Stripe(process.env.STRIPE_SECRET_KEY);
    await stripe.refunds.create({
      payment_intent: payment.providerRef,
      amount: amountMinor ?? undefined,
    });
  }

  const full = !amountMinor || amountMinor >= payment.amountMinor;
  await prisma.payment.update({
    where: { id: paymentId },
    data: { status: full ? "refunded" : "succeeded" },
  });
  await prisma.order.update({
    where: { id: payment.orderId },
    data: {
      paymentStatus: full ? "refunded" : "partially_refunded",
      status: full ? "refunded" : payment.order.status,
    },
  });
}
