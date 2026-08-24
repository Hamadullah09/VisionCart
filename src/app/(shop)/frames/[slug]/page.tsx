import { notFound } from "next/navigation";
import Link from "next/link";
import { getFrameBySlug, frameSizeLabel, listLensOptions } from "@/lib/catalog";
import { prisma } from "@/lib/db";
import { getSession } from "@/lib/session";
import { getSettingBool } from "@/lib/settings";
import { formatMoney } from "@/lib/money";
import { humanise } from "@/lib/constants";
import { summariseRx } from "@/lib/rx";
import LensBuilder, { type BuilderOption, type BuilderVariant } from "@/components/shop/LensBuilder";
import TryOnStudio from "@/components/tryon/TryOnStudio";
import type { TryOnFrame } from "@/components/tryon/types";

export async function generateMetadata({ params }: PageProps<"/frames/[slug]">) {
  const { slug } = await params;
  const frame = await getFrameBySlug(slug);
  if (!frame) return { title: "Frame not found" };
  return {
    title: frame.metaTitle || frame.name,
    description: frame.metaDesc || frame.description || undefined,
  };
}

export default async function FramePage({ params, searchParams }: PageProps<"/frames/[slug]">) {
  const { slug } = await params;
  const sp = await searchParams;
  const frame = await getFrameBySlug(slug);
  if (!frame) notFound();

  const session = await getSession();
  const [lensOptions, savedRx, tryOnEnabled, cameraEnabled, storePhotos] = await Promise.all([
    listLensOptions(),
    session ? loadSavedPrescriptions(session.userId) : Promise.resolve([]),
    getSettingBool("tryon.enabled"),
    getSettingBool("tryon.cameraEnabled"),
    getSettingBool("tryon.storeCustomerPhotos"),
  ]);

  const requestedVariant = typeof sp.variant === "string" ? sp.variant : undefined;
  const defaultVariant =
    frame.variants.find((v) => v.id === requestedVariant) ?? frame.variants[0];

  const gallery = frame.variants.flatMap((v) => v.images);
  const size = frameSizeLabel(frame);

  const builderVariants: BuilderVariant[] = frame.variants.map((v) => ({
    id: v.id,
    colorName: v.colorName,
    colorHex: v.colorHex,
    priceMinor: v.priceMinor ?? frame.basePriceMinor,
    stockQty: v.stockQty,
    imageUrl: v.images[0]?.url ?? v.tryOnImageUrl,
  }));

  const builderOptions: BuilderOption[] = lensOptions.map((o) => ({
    id: o.id,
    group: o.group,
    code: o.code,
    name: o.name,
    description: o.description,
    priceMinor: o.priceMinor,
    isDefault: o.isDefault,
    maxSphere: o.maxSphere,
    maxCylinder: o.maxCylinder,
  }));

  // Only this frame's colourways go into the inline mirror — the full range
  // lives on /try-on.
  const tryOnFrames: TryOnFrame[] = frame.variants
    .filter((v) => v.tryOnImageUrl)
    .map((v) => ({
      variantId: v.id,
      frameId: frame.id,
      slug: frame.slug,
      name: frame.name,
      brand: frame.brand?.name ?? null,
      colorName: v.colorName,
      colorHex: v.colorHex,
      overlayUrl: v.tryOnImageUrl,
      thumbUrl: v.images[0]?.thumbUrl ?? v.tryOnImageUrl,
      priceMinor: v.priceMinor ?? frame.basePriceMinor,
      compareAtMinor: frame.compareAtMinor,
      anchors: {
        leftX: v.anchorLeftX,
        leftY: v.anchorLeftY,
        rightX: v.anchorRightX,
        rightY: v.anchorRightY,
        scaleAdj: v.tryOnScaleAdj,
      },
      opacity: v.tryOnOpacity,
      shape: frame.shape,
      sizeBand: frame.sizeBand,
      totalWidthMm: frame.totalWidthMm,
    }));

  const price = defaultVariant?.priceMinor ?? frame.basePriceMinor;

  return (
    <div className="mx-auto max-w-7xl px-4 py-10">
      <nav className="mb-6 text-sm text-ink-500">
        <Link href="/frames" className="hover:text-brand-600">
          Frames
        </Link>
        <span className="mx-2">/</span>
        <span>{frame.name}</span>
      </nav>

      <div className="grid gap-10 lg:grid-cols-2">
        {/* Gallery */}
        <div className="space-y-3 lg:sticky lg:top-24 lg:self-start">
          <div className="card grid aspect-4/3 place-items-center overflow-hidden bg-ink-50 p-6">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src={gallery[0]?.url ?? defaultVariant?.tryOnImageUrl ?? ""}
              alt={gallery[0]?.alt ?? frame.name}
              className="max-h-full w-full object-contain"
            />
          </div>
          {gallery.length > 1 && (
            <div className="grid grid-cols-5 gap-2">
              {gallery.slice(0, 10).map((img) => (
                // eslint-disable-next-line @next/next/no-img-element
                <img
                  key={img.id}
                  src={img.thumbUrl ?? img.url}
                  alt={img.alt ?? frame.name}
                  className="card aspect-square w-full bg-white object-contain p-1"
                  loading="lazy"
                />
              ))}
            </div>
          )}
        </div>

        {/* Buy box */}
        <div>
          {frame.brand && (
            <p className="text-sm tracking-wide text-ink-500 uppercase">{frame.brand.name}</p>
          )}
          <h1 className="mt-1 text-3xl font-semibold">{frame.name}</h1>

          <div className="mt-3 flex items-baseline gap-3">
            <span className="text-2xl font-semibold">{formatMoney(price)}</span>
            {frame.compareAtMinor && frame.compareAtMinor > price && (
              <span className="text-lg text-ink-400 line-through">
                {formatMoney(frame.compareAtMinor)}
              </span>
            )}
            <span className="text-sm text-ink-500">frame — lenses added below</span>
          </div>

          {frame.description && <p className="mt-4 text-ink-700">{frame.description}</p>}

          <dl className="mt-5 grid grid-cols-2 gap-x-6 gap-y-2 text-sm">
            <Spec label="Shape" value={humanise(frame.shape)} />
            <Spec label="Material" value={humanise(frame.material)} />
            <Spec label="Rim" value={humanise(frame.rimType)} />
            <Spec label="Size" value={size} />
            <Spec label="Total width" value={frame.totalWidthMm ? `${frame.totalWidthMm} mm` : null} />
            <Spec label="Weight" value={frame.weightGrams ? `${frame.weightGrams} g` : null} />
            <Spec label="Suits" value={frame.faceShapes?.split(",").map(humanise).join(", ")} />
            <Spec label="SKU" value={frame.sku} />
          </dl>

          <div className="mt-8">
            <LensBuilder
              frameName={frame.name}
              variants={builderVariants}
              options={builderOptions}
              savedPrescriptions={savedRx}
              defaultVariantId={defaultVariant?.id}
              allowFrameOnly={frame.allowFrameOnly}
              requiresPrescription={frame.requiresPrescription}
            />
          </div>
        </div>
      </div>

      {/* Inline try-on */}
      {tryOnEnabled && tryOnFrames.length > 0 && (
        <section className="mt-16 border-t border-ink-200 pt-10">
          <h2 className="text-2xl font-semibold">See it on your face</h2>
          <p className="mt-1 text-sm text-ink-600">
            Upload a photo or use your camera. Nothing is uploaded unless you save it.
          </p>
          <div className="mt-6">
            <TryOnStudio
              frames={tryOnFrames}
              initialVariantId={defaultVariant?.id}
              canSave={Boolean(session) && storePhotos}
              cameraEnabled={cameraEnabled}
            />
          </div>
        </section>
      )}
    </div>
  );
}

function Spec({ label, value }: { label: string; value?: string | null }) {
  if (!value) return null;
  return (
    <div className="flex justify-between border-b border-ink-100 py-1">
      <dt className="text-ink-500">{label}</dt>
      <dd className="font-medium">{value}</dd>
    </div>
  );
}

async function loadSavedPrescriptions(userId: string) {
  const patient = await prisma.patient.findUnique({
    where: { userId },
    include: {
      prescriptions: {
        where: { status: { in: ["verified", "pending_verification", "draft"] } },
        orderBy: { issuedAt: "desc" },
        take: 5,
      },
    },
  });

  return (patient?.prescriptions ?? []).map((rx) => ({
    id: rx.id,
    label: `${rx.issuedAt.toLocaleDateString("en-GB")}${rx.prescriber ? ` — ${rx.prescriber}` : ""}`,
    summary: summariseRx(rx),
    status: rx.status,
  }));
}
