import Link from "next/link";
import { prisma } from "@/lib/db";
import { formatMoney } from "@/lib/money";
import { humanise, PRODUCT_STATUSES } from "@/lib/constants";
import StatusChip from "@/components/shop/StatusChip";

export const metadata = { title: "Frames" };

export default async function AdminFramesPage({ searchParams }: PageProps<"/admin/frames">) {
  const sp = await searchParams;
  const q = typeof sp.q === "string" ? sp.q : "";
  const status = typeof sp.status === "string" ? sp.status : "";
  const stock = typeof sp.stock === "string" ? sp.stock : "";

  const frames = await prisma.frame.findMany({
    where: {
      ...(q
        ? { OR: [{ name: { contains: q } }, { sku: { contains: q } }] }
        : {}),
      ...(status ? { status } : {}),
      ...(stock === "low" ? { variants: { some: { stockQty: { lte: 3 }, isActive: true } } } : {}),
    },
    include: {
      brand: { select: { name: true } },
      variants: { include: { images: { take: 1, orderBy: { position: "asc" } } } },
    },
    orderBy: [{ status: "asc" }, { updatedAt: "desc" }],
    take: 200,
  });

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold">Frames</h1>
          <p className="text-sm text-ink-600">{frames.length} shown</p>
        </div>
        <Link href="/admin/frames/new" className="btn-primary">
          New frame
        </Link>
      </header>

      <form method="get" className="card flex flex-wrap gap-3 p-4">
        <input name="q" defaultValue={q} placeholder="Name or SKU" className="field w-56" />
        <select name="status" defaultValue={status} className="field w-40">
          <option value="">Any status</option>
          {PRODUCT_STATUSES.map((s) => (
            <option key={s} value={s}>
              {humanise(s)}
            </option>
          ))}
        </select>
        <select name="stock" defaultValue={stock} className="field w-40">
          <option value="">Any stock</option>
          <option value="low">Low or out</option>
        </select>
        <button type="submit" className="btn-secondary">
          Filter
        </button>
        <Link href="/admin/frames" className="btn-secondary">
          Clear
        </Link>
      </form>

      <div className="table-wrap bg-white">
        <table className="table">
          <thead>
            <tr>
              <th></th>
              <th>Frame</th>
              <th>Brand</th>
              <th>Colours</th>
              <th>Stock</th>
              <th>Price</th>
              <th>Try-on</th>
              <th>Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {frames.map((f) => {
              const stockTotal = f.variants.reduce((s, v) => s + v.stockQty, 0);
              const tryOnReady = f.variants.filter((v) => v.tryOnImageUrl).length;
              const thumb =
                f.variants.flatMap((v) => v.images)[0]?.thumbUrl ??
                f.variants.find((v) => v.tryOnImageUrl)?.tryOnImageUrl;

              return (
                <tr key={f.id}>
                  <td className="w-14">
                    {thumb ? (
                      // eslint-disable-next-line @next/next/no-img-element
                      <img
                        src={thumb}
                        alt=""
                        className="h-10 w-12 rounded bg-ink-50 object-contain"
                      />
                    ) : (
                      <div className="h-10 w-12 rounded bg-ink-100" />
                    )}
                  </td>
                  <td>
                    <Link href={`/admin/frames/${f.id}`} className="font-medium hover:text-brand-600">
                      {f.name}
                    </Link>
                    <span className="block font-mono text-xs text-ink-500">{f.sku}</span>
                  </td>
                  <td className="text-ink-600">{f.brand?.name ?? "—"}</td>
                  <td>{f.variants.length}</td>
                  <td>
                    <span
                      className={`chip ${
                        stockTotal <= 0
                          ? "bg-rose-100 text-rose-800"
                          : stockTotal <= 5
                            ? "bg-amber-100 text-amber-800"
                            : "bg-ink-100 text-ink-700"
                      }`}
                    >
                      {stockTotal}
                    </span>
                  </td>
                  <td>{formatMoney(f.basePriceMinor)}</td>
                  <td className="text-xs text-ink-600">
                    {tryOnReady}/{f.variants.length}
                  </td>
                  <td>
                    <StatusChip status={f.status} />
                  </td>
                  <td className="text-right">
                    <Link href={`/admin/frames/${f.id}`} className="btn-secondary btn-sm">
                      Edit
                    </Link>
                  </td>
                </tr>
              );
            })}

            {frames.length === 0 && (
              <tr>
                <td colSpan={9} className="py-10 text-center text-ink-600">
                  Nothing matches. <Link href="/admin/frames/new" className="text-brand-600 underline">Add a frame</Link>.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
