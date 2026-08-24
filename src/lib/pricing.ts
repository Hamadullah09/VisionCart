import "server-only";
import { prisma } from "./db";
import { applyBps, clampNonNegative, CURRENCY } from "./money";
import { strongestSphere, strongestCylinder, type FlatRx } from "./rx";
import type { LensOption } from "@prisma/client";

/**
 * Single place where a price is decided. The storefront, the cart API and the
 * order writer all call this — a line total is never computed in a component,
 * so a tampered client payload cannot change what is charged.
 */

export type LineInput = {
  variantId: string;
  qty: number;
  lensOptionCodes: string[];
  rx?: FlatRx | null;
};

export type PricedLine = {
  variantId: string;
  qty: number;
  title: string;
  sku: string;
  imageUrl: string | null;
  /** Kept on the line so promotion targeting never needs a second query. */
  frameId: string;
  brandId: string | null;
  categoryIds: string[];
  framePriceMinor: number;
  lensPriceMinor: number;
  /** (frame + lens) x qty */
  totalMinor: number;
  lensOptions: LensOption[];
  lensSummary: string;
  warnings: string[];
};

/** Codes that only make sense with a real prescription. */
const RX_ONLY_GROUPS = new Set(["index", "type"]);

export async function loadLensOptions(codes: string[]): Promise<LensOption[]> {
  if (codes.length === 0) return [];
  const found = await prisma.lensOption.findMany({
    where: { code: { in: codes }, isActive: true },
    orderBy: [{ group: "asc" }, { position: "asc" }],
  });
  return found;
}

/**
 * Reject option combinations the lab cannot make. Returns human-readable
 * problems rather than throwing, so the UI can show them next to the choice.
 */
export function validateLensSelection(
  options: LensOption[],
  rx?: FlatRx | null,
): string[] {
  const problems: string[] = [];
  const codes = new Set(options.map((o) => o.code));

  for (const opt of options) {
    for (const req of splitCodes(opt.requires)) {
      if (!codes.has(req)) {
        problems.push(`${opt.name} also requires "${req}".`);
      }
    }
    for (const ex of splitCodes(opt.excludes)) {
      if (codes.has(ex)) {
        problems.push(`${opt.name} cannot be combined with "${ex}".`);
      }
    }

    if (rx) {
      const sph = strongestSphere(rx);
      const cyl = strongestCylinder(rx);
      if (opt.minSphere != null && sph < Math.abs(opt.minSphere)) {
        problems.push(`${opt.name} is only available from ${opt.minSphere.toFixed(2)} D.`);
      }
      if (opt.maxSphere != null && sph > Math.abs(opt.maxSphere)) {
        problems.push(
          `${opt.name} tops out at ${Math.abs(opt.maxSphere).toFixed(2)} D — your prescription is ${sph.toFixed(2)} D. Pick a thinner index.`,
        );
      }
      if (opt.maxCylinder != null && cyl > Math.abs(opt.maxCylinder)) {
        problems.push(
          `${opt.name} supports a cylinder up to ${Math.abs(opt.maxCylinder).toFixed(2)} D.`,
        );
      }
    } else if (RX_ONLY_GROUPS.has(opt.group)) {
      problems.push(`${opt.name} needs a prescription on the order.`);
    }
  }

  return problems;
}

export function splitCodes(value: string | null | undefined): string[] {
  return (value ?? "")
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean);
}

export function joinCodes(codes: string[]): string {
  return codes.filter(Boolean).join(",");
}

export async function priceLines(inputs: LineInput[]): Promise<PricedLine[]> {
  if (inputs.length === 0) return [];

  const variants = await prisma.frameVariant.findMany({
    where: { id: { in: inputs.map((i) => i.variantId) } },
    include: {
      frame: { include: { categories: { select: { categoryId: true } } } },
      images: { orderBy: { position: "asc" }, take: 1 },
    },
  });
  const byId = new Map(variants.map((v) => [v.id, v]));

  const allCodes = [...new Set(inputs.flatMap((i) => i.lensOptionCodes))];
  const options = await loadLensOptions(allCodes);
  const optByCode = new Map(options.map((o) => [o.code, o]));

  const lines: PricedLine[] = [];
  for (const input of inputs) {
    const variant = byId.get(input.variantId);
    if (!variant) continue; // silently drop lines whose product was deleted

    const qty = Math.max(1, Math.min(20, Math.trunc(input.qty) || 1));
    const framePriceMinor = variant.priceMinor ?? variant.frame.basePriceMinor;

    const chosen = input.lensOptionCodes
      .map((c) => optByCode.get(c))
      .filter((o): o is LensOption => Boolean(o));

    const lensPriceMinor = chosen.reduce((sum, o) => sum + o.priceMinor, 0);

    const warnings = validateLensSelection(chosen, input.rx);
    if (variant.stockQty <= 0) {
      warnings.push(`${variant.frame.name} (${variant.colorName}) is out of stock.`);
    } else if (variant.stockQty < qty) {
      warnings.push(
        `Only ${variant.stockQty} left of ${variant.frame.name} (${variant.colorName}).`,
      );
    }
    if (variant.frame.requiresPrescription && !input.rx && chosen.length === 0) {
      warnings.push(`${variant.frame.name} is sold with prescription lenses only.`);
    }

    lines.push({
      variantId: variant.id,
      qty,
      title: `${variant.frame.name} — ${variant.colorName}`,
      sku: variant.sku,
      imageUrl: variant.images[0]?.thumbUrl ?? variant.images[0]?.url ?? null,
      frameId: variant.frameId,
      brandId: variant.frame.brandId,
      categoryIds: variant.frame.categories.map((c) => c.categoryId),
      framePriceMinor,
      lensPriceMinor,
      totalMinor: (framePriceMinor + lensPriceMinor) * qty,
      lensOptions: chosen,
      lensSummary: chosen.map((o) => o.name).join(" · ") || "Frame only",
      warnings,
    });
  }

  return lines;
}

export type Totals = {
  currency: string;
  subtotalMinor: number;
  lensTotalMinor: number;
  discountMinor: number;
  shippingMinor: number;
  taxMinor: number;
  totalMinor: number;
};

export function sumLines(lines: PricedLine[]) {
  const subtotalMinor = lines.reduce((s, l) => s + l.framePriceMinor * l.qty, 0);
  const lensTotalMinor = lines.reduce((s, l) => s + l.lensPriceMinor * l.qty, 0);
  return { subtotalMinor, lensTotalMinor };
}

/**
 * Tax is charged on goods after discount but before shipping, which is the
 * common arrangement. TAX_INCLUSIVE=true instead treats displayed prices as
 * already containing tax and just reports the embedded portion.
 */
export function computeTotals(args: {
  lines: PricedLine[];
  discountMinor: number;
  shippingMinor: number;
  currency?: string;
}): Totals {
  const { subtotalMinor, lensTotalMinor } = sumLines(args.lines);
  const goods = subtotalMinor + lensTotalMinor;
  const discountMinor = Math.min(args.discountMinor, goods);
  const taxable = clampNonNegative(goods - discountMinor);

  const bps = Number(process.env.TAX_RATE_BPS || 0);
  const inclusive = process.env.TAX_INCLUSIVE === "true";
  const taxMinor = bps > 0 ? (inclusive ? taxable - Math.round((taxable * 10000) / (10000 + bps)) : applyBps(taxable, bps)) : 0;

  const totalMinor = inclusive
    ? taxable + args.shippingMinor
    : taxable + args.shippingMinor + taxMinor;

  return {
    currency: args.currency || CURRENCY,
    subtotalMinor,
    lensTotalMinor,
    discountMinor,
    shippingMinor: args.shippingMinor,
    taxMinor,
    totalMinor: clampNonNegative(totalMinor),
  };
}
