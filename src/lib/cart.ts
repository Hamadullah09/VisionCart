import "server-only";
import crypto from "node:crypto";
import { cookies } from "next/headers";
import { prisma } from "./db";
import { getSession } from "./session";
import { CART_COOKIE } from "./session";
import { priceLines, splitCodes, type PricedLine } from "./pricing";
import { computeTotals, type Totals } from "./pricing";
import { evaluatePromotions, type AppliedPromotion } from "./promotions";
import { quoteShipping } from "./shipping";
import { CURRENCY } from "./money";
import type { FlatRx } from "./rx";

const CART_MAX_AGE = 60 * 60 * 24 * 60; // 60 days

export type CartView = {
  cartId: string;
  lines: (PricedLine & { itemId: string })[];
  totals: Totals;
  promotions: AppliedPromotion[];
  promoCode: string | null;
  promoError?: string;
  itemCount: number;
  warnings: string[];
};

/** Read the cart token without creating one — safe in server components. */
export async function peekCartToken(): Promise<string | null> {
  const jar = await cookies();
  return jar.get(CART_COOKIE)?.value ?? null;
}

/**
 * Get or create the visitor's cart. Only call from a route handler or server
 * action — writing a cookie during a plain render is not allowed in Next.
 */
export async function getOrCreateCart() {
  const jar = await cookies();
  const session = await getSession();
  let token = jar.get(CART_COOKIE)?.value;

  if (token) {
    const existing = await prisma.cart.findUnique({ where: { token } });
    if (existing) {
      // Someone who shopped as a guest and then signed in keeps their bag.
      if (session && !existing.userId) {
        return prisma.cart.update({
          where: { id: existing.id },
          data: { userId: session.userId },
        });
      }
      return existing;
    }
  }

  token = crypto.randomBytes(16).toString("hex");
  const cart = await prisma.cart.create({
    data: { token, userId: session?.userId ?? null, currency: CURRENCY },
  });
  jar.set(CART_COOKIE, token, {
    httpOnly: true,
    sameSite: "lax",
    secure: process.env.NODE_ENV === "production",
    path: "/",
    maxAge: CART_MAX_AGE,
  });
  return cart;
}

/**
 * Price the cart from scratch on every read. Slower than trusting the stored
 * numbers, but it means a price change, a stock change or a promotion ending
 * is reflected the moment the customer looks at their bag.
 */
export async function buildCartView(cartId: string, opts: { country?: string } = {}): Promise<CartView> {
  const cart = await prisma.cart.findUnique({
    where: { id: cartId },
    include: { items: { orderBy: { createdAt: "asc" } } },
  });
  if (!cart) {
    return {
      cartId,
      lines: [],
      totals: computeTotals({ lines: [], discountMinor: 0, shippingMinor: 0 }),
      promotions: [],
      promoCode: null,
      itemCount: 0,
      warnings: [],
    };
  }

  const session = await getSession();

  const priced = await priceLines(
    cart.items.map((i) => ({
      variantId: i.variantId,
      qty: i.qty,
      lensOptionCodes: splitCodes(i.lensOptionCodes),
      rx: parseRxDraft(i.prescriptionDraft),
    })),
  );

  // priceLines drops lines whose variant vanished, so match back by variantId
  // in order rather than by index.
  const lines = priced.map((p) => {
    const item = cart.items.find((i) => i.variantId === p.variantId)!;
    return { ...p, itemId: item.id };
  });

  const promo = await evaluatePromotions({
    lines,
    code: cart.promoCode,
    userId: session?.userId ?? null,
    email: session?.email ?? null,
  });

  const goods = lines.reduce((s, l) => s + l.totalMinor, 0);
  const shipping = await quoteShipping({
    subtotalMinor: goods - promo.discountMinor,
    country: opts.country || "PK",
  });
  const shippingMinor = promo.freeShipping ? 0 : (shipping[0]?.priceMinor ?? 0);

  return {
    cartId: cart.id,
    lines,
    totals: computeTotals({
      lines,
      discountMinor: promo.discountMinor,
      shippingMinor,
      currency: cart.currency,
    }),
    promotions: promo.applied,
    promoCode: cart.promoCode,
    promoError: promo.codeError,
    itemCount: lines.reduce((s, l) => s + l.qty, 0),
    warnings: lines.flatMap((l) => l.warnings),
  };
}

export function parseRxDraft(raw: string | null): FlatRx | null {
  if (!raw) return null;
  try {
    return JSON.parse(raw) as FlatRx;
  } catch {
    return null;
  }
}

export async function addToCart(input: {
  cartId: string;
  variantId: string;
  qty?: number;
  lensOptionCodes?: string[];
  prescriptionDraft?: unknown;
  prescriptionId?: string | null;
  tryOnSnapshotId?: string | null;
}) {
  const codes = (input.lensOptionCodes ?? []).slice().sort().join(",");

  // Same frame + same lens build = bump the quantity instead of stacking rows.
  const twin = await prisma.cartItem.findFirst({
    where: {
      cartId: input.cartId,
      variantId: input.variantId,
      lensOptionCodes: codes || null,
      prescriptionId: input.prescriptionId ?? null,
    },
  });

  if (twin && !input.prescriptionDraft) {
    return prisma.cartItem.update({
      where: { id: twin.id },
      data: { qty: Math.min(20, twin.qty + (input.qty ?? 1)) },
    });
  }

  return prisma.cartItem.create({
    data: {
      cartId: input.cartId,
      variantId: input.variantId,
      qty: Math.max(1, Math.min(20, input.qty ?? 1)),
      lensOptionCodes: codes || null,
      prescriptionDraft: input.prescriptionDraft
        ? JSON.stringify(input.prescriptionDraft)
        : null,
      prescriptionId: input.prescriptionId ?? null,
      tryOnSnapshotId: input.tryOnSnapshotId ?? null,
    },
  });
}

export async function updateCartItem(itemId: string, qty: number) {
  if (qty <= 0) return prisma.cartItem.delete({ where: { id: itemId } });
  return prisma.cartItem.update({
    where: { id: itemId },
    data: { qty: Math.min(20, Math.trunc(qty)) },
  });
}

export async function removeCartItem(itemId: string) {
  return prisma.cartItem.delete({ where: { id: itemId } });
}

export async function setPromoCode(cartId: string, code: string | null) {
  return prisma.cart.update({
    where: { id: cartId },
    data: { promoCode: code ? code.trim().toUpperCase() : null },
  });
}

export async function clearCartCookie() {
  const jar = await cookies();
  jar.delete(CART_COOKIE);
}
