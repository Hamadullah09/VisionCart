import "server-only";
import { prisma } from "./db";
import { applyBps } from "./money";
import { splitCodes, type PricedLine } from "./pricing";
import type { Promotion } from "@prisma/client";

/**
 * Promotion engine. Marketing configures rows in the `Promotion` table from
 * the back office; nothing here is hard-coded to a campaign, so a new deal is
 * a form submission rather than a deploy.
 */

export type AppliedPromotion = {
  id: string;
  name: string;
  code: string | null;
  kind: string;
  discountMinor: number;
  freeShipping: boolean;
  description: string;
};

export type PromotionResult = {
  applied: AppliedPromotion[];
  discountMinor: number;
  freeShipping: boolean;
  /** Set when the customer typed a code that was rejected. */
  codeError?: string;
};

function isLive(p: Promotion, now: Date): boolean {
  if (!p.isActive) return false;
  if (p.startsAt && p.startsAt > now) return false;
  if (p.endsAt && p.endsAt < now) return false;
  if (p.usageLimit != null && p.usageCount >= p.usageLimit) return false;
  return true;
}

/** Lines this promotion is allowed to discount. Empty targeting = everything. */
function eligibleLines(p: Promotion, lines: PricedLine[]): PricedLine[] {
  const brands = splitCodes(p.brandIds);
  const cats = splitCodes(p.categoryIds);
  const frames = splitCodes(p.frameIds);
  if (brands.length === 0 && cats.length === 0 && frames.length === 0) return lines;

  return lines.filter(
    (l) =>
      (l.brandId && brands.includes(l.brandId)) ||
      frames.includes(l.frameId) ||
      l.categoryIds.some((c) => cats.includes(c)),
  );
}

function lineGoods(l: PricedLine): number {
  return (l.framePriceMinor + l.lensPriceMinor) * l.qty;
}

/** Why a promotion did not apply, in words a customer can act on. */
function unmetCondition(
  p: Promotion,
  eligible: PricedLine[],
  eligibleSubtotal: number,
  isFirstOrder: boolean,
): string | null {
  if (eligible.length === 0) return "This code doesn't apply to anything in your bag.";
  if (eligibleSubtotal < p.minSubtotalMinor) {
    return `Spend a little more to unlock "${p.name}".`;
  }
  const qty = eligible.reduce((s, l) => s + l.qty, 0);
  if (qty < p.minQty) {
    return `"${p.name}" needs at least ${p.minQty} item${p.minQty > 1 ? "s" : ""}.`;
  }
  if (p.firstOrderOnly && !isFirstOrder) {
    return `"${p.name}" is for first orders only.`;
  }
  return null;
}

function discountFor(p: Promotion, eligible: PricedLine[]): { minor: number; freeShipping: boolean } {
  const eligibleSubtotal = eligible.reduce((s, l) => s + lineGoods(l), 0);
  let minor = 0;
  let freeShipping = false;

  switch (p.kind) {
    case "percent_off":
      minor = applyBps(eligibleSubtotal, p.value);
      break;

    case "amount_off":
      minor = p.value;
      break;

    case "free_shipping":
      freeShipping = true;
      break;

    case "bogo": {
      // Expand to a flat list of unit prices, sort ascending, and make every
      // second unit free — the customer always keeps the more expensive one.
      const units: number[] = [];
      for (const l of eligible) {
        for (let i = 0; i < l.qty; i++) units.push(l.framePriceMinor + l.lensPriceMinor);
      }
      units.sort((a, b) => a - b);
      const freeCount = Math.floor(units.length / 2);
      minor = units.slice(0, freeCount).reduce((s, u) => s + u, 0);
      break;
    }

    case "free_lens_upgrade":
      // Waives what the customer paid for lens choices, capped below.
      minor = eligible.reduce((s, l) => s + l.lensPriceMinor * l.qty, 0);
      break;

    case "bundle":
      // `value` is the bundle price the eligible items are re-priced to.
      minor = Math.max(0, eligibleSubtotal - p.value);
      break;
  }

  if (p.maxDiscountMinor != null) minor = Math.min(minor, p.maxDiscountMinor);
  return { minor: Math.max(0, Math.min(minor, eligibleSubtotal)), freeShipping };
}

export async function evaluatePromotions(args: {
  lines: PricedLine[];
  code?: string | null;
  userId?: string | null;
  email?: string | null;
}): Promise<PromotionResult> {
  const { lines } = args;
  if (lines.length === 0) return { applied: [], discountMinor: 0, freeShipping: false };

  const now = new Date();
  const code = args.code?.trim().toUpperCase() || null;

  const candidates = await prisma.promotion.findMany({
    where: {
      isActive: true,
      OR: [{ code: null }, ...(code ? [{ code }] : [])],
    },
    orderBy: [{ priority: "desc" }, { createdAt: "asc" }],
  });

  const isFirstOrder = await firstOrderCheck(args.userId, args.email);

  let codeError: string | undefined;
  const usable: { promo: Promotion; discountMinor: number; freeShipping: boolean }[] = [];

  for (const p of candidates) {
    const eligible = eligibleLines(p, lines);
    const eligibleSubtotal = eligible.reduce((s, l) => s + lineGoods(l), 0);
    const live = isLive(p, now);
    const unmet = unmetCondition(p, eligible, eligibleSubtotal, isFirstOrder);

    if (!live || unmet) {
      // Only explain failures for a code the customer deliberately typed.
      if (p.code && p.code === code) {
        codeError = !live
          ? `"${p.code}" has expired or is no longer available.`
          : unmet!;
      }
      continue;
    }

    if (p.usageLimitPerUser != null && args.userId) {
      const used = await prisma.order.count({
        where: { promotionId: p.id, userId: args.userId, status: { not: "cancelled" } },
      });
      if (used >= p.usageLimitPerUser) {
        if (p.code === code) codeError = `You have already used "${p.code}".`;
        continue;
      }
    }

    const { minor, freeShipping } = discountFor(p, eligible);
    if (minor <= 0 && !freeShipping) continue;
    usable.push({ promo: p, discountMinor: minor, freeShipping });
  }

  // Highest priority wins; lower-priority ones only ride along if stackable.
  usable.sort((a, b) =>
    b.promo.priority - a.promo.priority || b.discountMinor - a.discountMinor,
  );

  const applied: AppliedPromotion[] = [];
  const appliedPromos: Promotion[] = [];
  for (const [i, u] of usable.entries()) {
    // The best offer always lands. Anything after it needs every promotion in
    // play — itself included — to permit stacking.
    if (i > 0 && (!u.promo.stackable || !appliedPromos.every((p) => p.stackable))) continue;
    appliedPromos.push(u.promo);
    applied.push({
      id: u.promo.id,
      name: u.promo.name,
      code: u.promo.code,
      kind: u.promo.kind,
      discountMinor: u.discountMinor,
      freeShipping: u.freeShipping,
      description: u.promo.description || describe(u.promo),
    });
  }

  return {
    applied,
    discountMinor: applied.reduce((s, a) => s + a.discountMinor, 0),
    freeShipping: applied.some((a) => a.freeShipping),
    codeError: applied.some((a) => a.code === code) ? undefined : codeError,
  };
}

async function firstOrderCheck(userId?: string | null, email?: string | null): Promise<boolean> {
  if (!userId && !email) return true;
  const count = await prisma.order.count({
    where: {
      status: { not: "cancelled" },
      OR: [
        ...(userId ? [{ userId }] : []),
        ...(email ? [{ email: email.toLowerCase() }] : []),
      ],
    },
  });
  return count === 0;
}

/** Fallback storefront copy when marketing didn't write a description. */
export function describe(p: Promotion): string {
  switch (p.kind) {
    case "percent_off":
      return `${(p.value / 100).toFixed(p.value % 100 === 0 ? 0 : 1)}% off`;
    case "amount_off":
      return "Money off your order";
    case "free_shipping":
      return "Free delivery";
    case "bogo":
      return "Buy one, get one free";
    case "free_lens_upgrade":
      return "Free lens upgrade";
    case "bundle":
      return "Bundle price";
    default:
      return p.name;
  }
}

/** Active, code-free promotions for the storefront banner strip. */
export async function activeBanners() {
  const now = new Date();
  return prisma.promotion.findMany({
    where: {
      isActive: true,
      bannerText: { not: null },
      AND: [
        { OR: [{ startsAt: null }, { startsAt: { lte: now } }] },
        { OR: [{ endsAt: null }, { endsAt: { gte: now } }] },
      ],
    },
    orderBy: { priority: "desc" },
    take: 3,
  });
}
