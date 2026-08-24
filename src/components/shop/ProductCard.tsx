import Link from "next/link";
import { formatMoney } from "@/lib/money";
import { humanise } from "@/lib/constants";
import { frameSizeLabel, primaryImage, type FrameCard } from "@/lib/catalog";

export default function ProductCard({ frame }: { frame: FrameCard }) {
  const image = primaryImage(frame);
  const price = frame.variants[0]?.priceMinor ?? frame.basePriceMinor;
  const onSale = frame.compareAtMinor != null && frame.compareAtMinor > price;
  const size = frameSizeLabel(frame);
  const inStock = frame.variants.some((v) => v.stockQty > 0);
  const canTryOn = frame.variants.some((v) => v.tryOnImageUrl);

  return (
    <article className="group card overflow-hidden transition hover:shadow-md">
      <Link href={`/frames/${frame.slug}`} className="block">
        <div className="relative aspect-4/3 bg-ink-50">
          {image ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img
              src={image.url}
              alt={image.alt}
              className="h-full w-full object-contain p-3 transition group-hover:scale-[1.03]"
              loading="lazy"
            />
          ) : (
            <div className="grid h-full place-items-center text-xs text-ink-400">No image yet</div>
          )}

          {onSale && (
            <span className="chip absolute top-2 left-2 bg-rose-600 text-white">Sale</span>
          )}
          {!inStock && (
            <span className="chip absolute top-2 right-2 bg-ink-800 text-white">Out of stock</span>
          )}
        </div>
      </Link>

      <div className="p-4">
        {frame.brand && (
          <p className="text-xs tracking-wide text-ink-500 uppercase">{frame.brand.name}</p>
        )}
        <Link href={`/frames/${frame.slug}`} className="block">
          <h3 className="mt-0.5 font-medium hover:text-brand-600">{frame.name}</h3>
        </Link>

        <p className="mt-1 text-xs text-ink-500">
          {[humanise(frame.shape), humanise(frame.material), size].filter(Boolean).join(" · ")}
        </p>

        <div className="mt-2 flex items-baseline gap-2">
          <span className="font-semibold">{formatMoney(price)}</span>
          {onSale && (
            <span className="text-sm text-ink-400 line-through">
              {formatMoney(frame.compareAtMinor!)}
            </span>
          )}
        </div>

        {/* Colour swatches double as a hint that more options exist. */}
        {frame.variants.length > 1 && (
          <div className="mt-3 flex flex-wrap gap-1.5">
            {frame.variants.slice(0, 6).map((v) => (
              <span
                key={v.id}
                title={v.colorName}
                className="h-4 w-4 rounded-full border border-ink-200"
                style={{ background: v.colorHex || "#ccc" }}
              />
            ))}
            {frame.variants.length > 6 && (
              <span className="text-xs text-ink-400">+{frame.variants.length - 6}</span>
            )}
          </div>
        )}

        {canTryOn && (
          <Link
            href={`/try-on?variant=${frame.variants.find((v) => v.tryOnImageUrl)?.id}`}
            className="mt-3 inline-block text-sm font-medium text-brand-600 hover:text-brand-700"
          >
            Try on →
          </Link>
        )}
      </div>
    </article>
  );
}
