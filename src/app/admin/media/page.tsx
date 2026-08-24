import { prisma } from "@/lib/db";
import BulkUploader from "@/components/admin/BulkUploader";

export const metadata = { title: "Media" };

export default async function AdminMediaPage({ searchParams }: PageProps<"/admin/media">) {
  const sp = await searchParams;
  const q = typeof sp.q === "string" ? sp.q : "";

  const assets = await prisma.mediaAsset.findMany({
    where: q ? { OR: [{ filename: { contains: q } }, { tags: { contains: q } }] } : {},
    orderBy: { createdAt: "desc" },
    take: 120,
  });

  return (
    <div className="space-y-8">
      <header>
        <h1 className="text-2xl font-semibold">Media library</h1>
        <p className="text-sm text-ink-600">
          Drop a whole shoot in at once. Images are resized, converted to WebP and thumbnailed
          automatically — attach them to a colourway from its frame page.
        </p>
      </header>

      <section className="card p-5">
        <BulkUploader
          label="Drop product photos here"
          hint="Any number at once. JPG, PNG or WebP up to 15 MB each."
        />
      </section>

      <section>
        <div className="flex flex-wrap items-baseline justify-between gap-3">
          <h2 className="font-semibold">Library ({assets.length})</h2>
          <form method="get" className="flex gap-2">
            <input
              name="q"
              defaultValue={q}
              placeholder="Search filename or tag"
              className="field w-56"
            />
            <button type="submit" className="btn-secondary btn-sm">
              Search
            </button>
          </form>
        </div>

        {assets.length === 0 ? (
          <p className="mt-6 text-sm text-ink-600">Nothing uploaded yet.</p>
        ) : (
          <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4 lg:grid-cols-6">
            {assets.map((a) => (
              <a
                key={a.id}
                href={a.url}
                target="_blank"
                rel="noreferrer noopener"
                className="card overflow-hidden bg-white p-2 transition hover:shadow-md"
              >
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src={a.thumbUrl ?? a.url}
                  alt={a.filename}
                  className="aspect-square w-full bg-ink-50 object-contain"
                  loading="lazy"
                />
                <p className="mt-1.5 truncate text-xs" title={a.filename}>
                  {a.filename}
                </p>
                <p className="text-[11px] text-ink-400">
                  {a.width}×{a.height} · {Math.round((a.sizeBytes ?? 0) / 1024)} KB
                </p>
                {a.tags && <p className="truncate text-[11px] text-brand-600">{a.tags}</p>}
              </a>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}
