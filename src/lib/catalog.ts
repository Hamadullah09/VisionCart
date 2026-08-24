import "server-only";
import { prisma } from "./db";
import type { Prisma } from "@prisma/client";
import type { TryOnFrame } from "@/components/tryon/types";

/** Shared catalogue reads. Keeps page components free of query plumbing. */

export type FrameFilters = {
  q?: string;
  gender?: string;
  shape?: string;
  material?: string;
  rimType?: string;
  brand?: string;
  category?: string;
  faceShape?: string;
  sizeBand?: string;
  minPrice?: number;
  maxPrice?: number;
  sort?: "featured" | "price_asc" | "price_desc" | "newest";
  page?: number;
  perPage?: number;
};

export const frameCardInclude = {
  brand: { select: { name: true, slug: true } },
  variants: {
    where: { isActive: true },
    orderBy: { position: "asc" },
    include: { images: { orderBy: { position: "asc" } } },
  },
} satisfies Prisma.FrameInclude;

export type FrameCard = Prisma.FrameGetPayload<{ include: typeof frameCardInclude }>;

export async function listFrames(filters: FrameFilters) {
  const perPage = Math.min(60, filters.perPage ?? 24);
  const page = Math.max(1, filters.page ?? 1);

  const where: Prisma.FrameWhereInput = { status: "active" };

  if (filters.q) {
    // SQLite has no case-insensitive `mode`, so `contains` is already
    // case-insensitive there; on Postgres this stays exact-case. Good enough
    // for a shop of this size — swap in full-text search when it isn't.
    where.OR = [
      { name: { contains: filters.q } },
      { sku: { contains: filters.q } },
      { description: { contains: filters.q } },
      { brand: { name: { contains: filters.q } } },
    ];
  }
  if (filters.gender) where.gender = filters.gender;
  if (filters.shape) where.shape = filters.shape;
  if (filters.material) where.material = filters.material;
  if (filters.rimType) where.rimType = filters.rimType;
  if (filters.sizeBand) where.sizeBand = filters.sizeBand;
  if (filters.brand) where.brand = { slug: filters.brand };
  if (filters.category) where.categories = { some: { category: { slug: filters.category } } };
  if (filters.faceShape) where.faceShapes = { contains: filters.faceShape };

  if (filters.minPrice != null || filters.maxPrice != null) {
    where.basePriceMinor = {
      ...(filters.minPrice != null ? { gte: filters.minPrice } : {}),
      ...(filters.maxPrice != null ? { lte: filters.maxPrice } : {}),
    };
  }

  const orderBy: Prisma.FrameOrderByWithRelationInput[] =
    filters.sort === "price_asc"
      ? [{ basePriceMinor: "asc" }]
      : filters.sort === "price_desc"
        ? [{ basePriceMinor: "desc" }]
        : filters.sort === "newest"
          ? [{ createdAt: "desc" }]
          : [{ isFeatured: "desc" }, { position: "asc" }, { createdAt: "desc" }];

  const [items, total] = await Promise.all([
    prisma.frame.findMany({
      where,
      include: frameCardInclude,
      orderBy,
      skip: (page - 1) * perPage,
      take: perPage,
    }),
    prisma.frame.count({ where }),
  ]);

  return { items, total, page, perPage, pages: Math.max(1, Math.ceil(total / perPage)) };
}

export async function getFrameBySlug(slug: string) {
  return prisma.frame.findFirst({
    where: { slug, status: "active" },
    include: {
      brand: true,
      categories: { include: { category: true } },
      variants: {
        where: { isActive: true },
        orderBy: { position: "asc" },
        include: { images: { orderBy: { position: "asc" } } },
      },
    },
  });
}

/** Filter facets, counted against the live catalogue so nothing dead is shown. */
export async function getFacets() {
  const [brands, shapes, materials, categories] = await Promise.all([
    prisma.brand.findMany({
      where: { isActive: true, frames: { some: { status: "active" } } },
      select: { name: true, slug: true, _count: { select: { frames: true } } },
      orderBy: { name: "asc" },
    }),
    prisma.frame.groupBy({
      by: ["shape"],
      where: { status: "active", shape: { not: null } },
      _count: true,
    }),
    prisma.frame.groupBy({
      by: ["material"],
      where: { status: "active", material: { not: null } },
      _count: true,
    }),
    prisma.category.findMany({
      where: { frames: { some: { frame: { status: "active" } } } },
      select: { name: true, slug: true },
      orderBy: { position: "asc" },
    }),
  ]);

  return { brands, shapes, materials, categories };
}

/**
 * Every colourway that has try-on artwork, shaped for the canvas. The studio
 * needs a flat list rather than the nested frame/variant tree.
 */
export async function listTryOnFrames(limit = 60): Promise<TryOnFrame[]> {
  const variants = await prisma.frameVariant.findMany({
    where: {
      isActive: true,
      tryOnImageUrl: { not: null },
      frame: { status: "active" },
    },
    include: {
      frame: { include: { brand: { select: { name: true } } } },
      images: { orderBy: { position: "asc" }, take: 1 },
    },
    orderBy: [{ frame: { isFeatured: "desc" } }, { position: "asc" }],
    take: limit,
  });

  return variants.map((v) => ({
    variantId: v.id,
    frameId: v.frameId,
    slug: v.frame.slug,
    name: v.frame.name,
    brand: v.frame.brand?.name ?? null,
    colorName: v.colorName,
    colorHex: v.colorHex,
    overlayUrl: v.tryOnImageUrl,
    thumbUrl: v.images[0]?.thumbUrl ?? v.images[0]?.url ?? v.tryOnImageUrl,
    priceMinor: v.priceMinor ?? v.frame.basePriceMinor,
    compareAtMinor: v.frame.compareAtMinor,
    anchors: {
      leftX: v.anchorLeftX,
      leftY: v.anchorLeftY,
      rightX: v.anchorRightX,
      rightY: v.anchorRightY,
      scaleAdj: v.tryOnScaleAdj,
    },
    opacity: v.tryOnOpacity,
    shape: v.frame.shape,
    sizeBand: v.frame.sizeBand,
    totalWidthMm: v.frame.totalWidthMm,
  }));
}

/** Lens builder options grouped for the storefront wizard. */
export async function listLensOptions() {
  return prisma.lensOption.findMany({
    where: { isActive: true },
    orderBy: [{ group: "asc" }, { position: "asc" }],
  });
}

/** Primary image for a frame card, falling back through the variant list. */
export function primaryImage(frame: FrameCard): { url: string; alt: string } | null {
  for (const v of frame.variants) {
    const img = v.images.find((i) => i.role === "primary") ?? v.images[0];
    if (img) return { url: img.url, alt: img.alt || `${frame.name} in ${v.colorName}` };
    if (v.tryOnImageUrl) return { url: v.tryOnImageUrl, alt: `${frame.name} in ${v.colorName}` };
  }
  return null;
}

export function frameSizeLabel(frame: {
  lensWidthMm: number | null;
  bridgeWidthMm: number | null;
  templeLengthMm: number | null;
}): string | null {
  if (!frame.lensWidthMm || !frame.bridgeWidthMm) return null;
  const temple = frame.templeLengthMm ? `-${frame.templeLengthMm}` : "";
  return `${frame.lensWidthMm}□${frame.bridgeWidthMm}${temple}`;
}
