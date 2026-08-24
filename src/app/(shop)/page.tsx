import Link from "next/link";
import { listFrames } from "@/lib/catalog";
import { activeBanners, describe } from "@/lib/promotions";
import { getSettings } from "@/lib/settings";
import ProductCard from "@/components/shop/ProductCard";

export default async function HomePage() {
  const [featured, newest, promos, settings] = await Promise.all([
    listFrames({ sort: "featured", perPage: 8 }),
    listFrames({ sort: "newest", perPage: 4 }),
    activeBanners(),
    getSettings(),
  ]);

  return (
    <>
      {/* Hero */}
      <section className="border-b border-ink-200 bg-gradient-to-b from-ink-50 to-white">
        <div className="mx-auto grid max-w-7xl items-center gap-10 px-4 py-16 lg:grid-cols-2 lg:py-24">
          <div>
            <p className="text-sm font-semibold tracking-wide text-brand-600 uppercase">
              {settings["store.tagline"]}
            </p>
            <h1 className="mt-3 text-4xl font-semibold tracking-tight text-balance sm:text-5xl">
              See how they look before you buy them.
            </h1>
            <p className="mt-4 max-w-lg text-lg text-ink-600">
              Upload a photo or open your camera, and every frame in our range lands on your face in
              the right size — with your pupillary distance measured while you browse.
            </p>
            <div className="mt-8 flex flex-wrap gap-3">
              <Link href="/try-on" className="btn-accent px-6 py-3 text-base">
                Start virtual try-on
              </Link>
              <Link href="/frames" className="btn-secondary px-6 py-3 text-base">
                Browse all frames
              </Link>
            </div>
            <p className="mt-4 text-xs text-ink-500">
              Your photo never leaves your device unless you choose to save it.
            </p>
          </div>

          <div className="grid grid-cols-2 gap-3">
            {featured.items.slice(0, 4).map((f) => {
              const variant = f.variants.find((v) => v.tryOnImageUrl) ?? f.variants[0];
              return (
                <Link
                  key={f.id}
                  href={`/frames/${f.slug}`}
                  className="card grid aspect-4/3 place-items-center bg-white p-4 transition hover:shadow-md"
                >
                  {variant?.tryOnImageUrl ? (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img
                      src={variant.tryOnImageUrl}
                      alt={f.name}
                      className="max-h-full w-full object-contain"
                    />
                  ) : (
                    <span className="text-sm text-ink-400">{f.name}</span>
                  )}
                </Link>
              );
            })}
          </div>
        </div>
      </section>

      {/* Live deals */}
      {promos.length > 0 && (
        <section className="mx-auto max-w-7xl px-4 py-12">
          <div className="flex items-baseline justify-between">
            <h2 className="text-2xl font-semibold">On offer right now</h2>
            <Link href="/deals" className="text-sm font-medium text-brand-600">
              All deals →
            </Link>
          </div>
          <div className="mt-5 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {promos.map((p) => (
              <div
                key={p.id}
                className="rounded-2xl p-5 text-white"
                style={{ background: p.bannerColor || "#0a67a1" }}
              >
                <p className="text-lg font-semibold">{p.name}</p>
                <p className="mt-1 text-sm text-white/85">{p.description || describe(p)}</p>
                {p.code && (
                  <p className="mt-3 inline-block rounded bg-white/20 px-2 py-1 font-mono text-sm tracking-wider">
                    {p.code}
                  </p>
                )}
                {p.endsAt && (
                  <p className="mt-3 text-xs text-white/75">
                    Ends {p.endsAt.toLocaleDateString("en-GB", { day: "numeric", month: "short" })}
                  </p>
                )}
              </div>
            ))}
          </div>
        </section>
      )}

      {/* How it works */}
      <section className="border-y border-ink-200 bg-ink-50">
        <div className="mx-auto grid max-w-7xl gap-8 px-4 py-14 sm:grid-cols-3">
          {[
            [
              "1. Try them on",
              "Upload a photo or use your camera. Frames are scaled to your face automatically.",
            ],
            [
              "2. Add your prescription",
              "Type it in, or upload a photo of the paper one and our optician will read it.",
            ],
            [
              "3. We make and ship",
              `Lenses cut in our lab and delivered, with ${settings["store.returnDays"]} days to change your mind.`,
            ],
          ].map(([title, body]) => (
            <div key={title}>
              <h3 className="font-semibold">{title}</h3>
              <p className="mt-2 text-sm text-ink-600">{body}</p>
            </div>
          ))}
        </div>
      </section>

      {/* Featured */}
      <section className="mx-auto max-w-7xl px-4 py-14">
        <div className="flex items-baseline justify-between">
          <h2 className="text-2xl font-semibold">Popular this season</h2>
          <Link href="/frames" className="text-sm font-medium text-brand-600">
            See all →
          </Link>
        </div>
        <div className="mt-6 grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
          {featured.items.map((f) => (
            <ProductCard key={f.id} frame={f} />
          ))}
        </div>
      </section>

      {/* New in */}
      {newest.items.length > 0 && (
        <section className="mx-auto max-w-7xl px-4 pb-16">
          <h2 className="text-2xl font-semibold">New in</h2>
          <div className="mt-6 grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
            {newest.items.map((f) => (
              <ProductCard key={f.id} frame={f} />
            ))}
          </div>
        </section>
      )}
    </>
  );
}
