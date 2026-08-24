import Link from "next/link";
import { notFound } from "next/navigation";
import { prisma } from "@/lib/db";
import { fromMinor } from "@/lib/money";
import FrameForm from "@/components/admin/FrameForm";
import VariantEditor, { type EditableVariant } from "@/components/admin/VariantEditor";
import { deleteFrameAction, deleteVariantAction } from "@/app/actions/admin";

export const metadata = { title: "Edit frame" };

export default async function EditFramePage({ params }: PageProps<"/admin/frames/[id]">) {
  const { id } = await params;

  const [frame, brands] = await Promise.all([
    prisma.frame.findUnique({
      where: { id },
      include: {
        variants: {
          orderBy: { position: "asc" },
          include: { images: { orderBy: { position: "asc" } } },
        },
      },
    }),
    prisma.brand.findMany({
      where: { isActive: true },
      select: { id: true, name: true },
      orderBy: { name: "asc" },
    }),
  ]);

  if (!frame) notFound();

  const variants: EditableVariant[] = frame.variants.map((v) => ({
    id: v.id,
    frameId: v.frameId,
    sku: v.sku,
    colorName: v.colorName,
    colorHex: v.colorHex,
    barcode: v.barcode,
    priceMajor: v.priceMinor == null ? "" : String(fromMinor(v.priceMinor)),
    stockQty: v.stockQty,
    lowStockAt: v.lowStockAt,
    isActive: v.isActive,
    position: v.position,
    tryOnImageUrl: v.tryOnImageUrl,
    anchorLeftX: v.anchorLeftX,
    anchorLeftY: v.anchorLeftY,
    anchorRightX: v.anchorRightX,
    anchorRightY: v.anchorRightY,
    tryOnScaleAdj: v.tryOnScaleAdj,
    tryOnOpacity: v.tryOnOpacity,
    images: v.images.map((i) => ({
      id: i.id,
      url: i.url,
      thumbUrl: i.thumbUrl,
      role: i.role,
    })),
  }));

  const missingTryOn = variants.filter((v) => !v.tryOnImageUrl).length;

  return (
    <div className="max-w-5xl space-y-8">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <Link href="/admin/frames" className="text-sm text-brand-600">
            ← Frames
          </Link>
          <h1 className="mt-1 text-2xl font-semibold">{frame.name}</h1>
          <p className="font-mono text-sm text-ink-500">{frame.sku}</p>
        </div>
        <div className="flex gap-2">
          <Link href={`/frames/${frame.slug}`} className="btn-secondary btn-sm" target="_blank">
            View in shop
          </Link>
          <form action={deleteFrameAction}>
            <input type="hidden" name="id" value={frame.id} />
            <button type="submit" className="btn-danger btn-sm">
              Archive
            </button>
          </form>
        </div>
      </div>

      {missingTryOn > 0 && (
        <p className="rounded-lg bg-amber-50 px-4 py-3 text-sm text-amber-800">
          {missingTryOn} of {variants.length} colourways have no try-on artwork, so they won&apos;t
          appear in the virtual mirror. Add a transparent PNG under each colourway&apos;s
          &ldquo;Virtual try-on&rdquo; tab.
        </p>
      )}

      <FrameForm frame={frame} brands={brands} />

      <section className="space-y-3">
        <div className="flex items-baseline justify-between">
          <h2 className="text-lg font-semibold">Colourways</h2>
          <p className="text-sm text-ink-600">
            Stock, photos and try-on calibration live here.
          </p>
        </div>

        {variants.map((v) => (
          <div key={v.id}>
            <VariantEditor variant={v} frameId={frame.id} />
            <form action={deleteVariantAction} className="mt-1 text-right">
              <input type="hidden" name="id" value={v.id} />
              <input type="hidden" name="frameId" value={frame.id} />
              <button
                type="submit"
                className="text-xs text-ink-400 underline underline-offset-2 hover:text-rose-600"
              >
                Remove this colourway
              </button>
            </form>
          </div>
        ))}

        <div className="pt-2">
          <h3 className="mb-2 text-sm font-semibold">Add a colourway</h3>
          <VariantEditor variant={null} frameId={frame.id} />
        </div>
      </section>
    </div>
  );
}
