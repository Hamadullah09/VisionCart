import Link from "next/link";
import { prisma } from "@/lib/db";
import { formatMoney } from "@/lib/money";
import { PROMOTION_KIND_LABELS, type PromotionKind } from "@/lib/constants";
import { togglePromotionAction } from "@/app/actions/admin";

export const metadata = { title: "Promotions" };

export default async function AdminPromotionsPage() {
  const promotions = await prisma.promotion.findMany({
    orderBy: [{ isActive: "desc" }, { priority: "desc" }, { createdAt: "desc" }],
    include: { _count: { select: { orders: true } } },
  });

  const now = new Date();

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold">Promotions</h1>
          <p className="text-sm text-ink-600">
            Deals go live the moment you save — no deploy, no developer.
          </p>
        </div>
        <Link href="/admin/promotions/new" className="btn-primary">
          New deal
        </Link>
      </header>

      <div className="table-wrap bg-white">
        <table className="table">
          <thead>
            <tr>
              <th>Deal</th>
              <th>Code</th>
              <th>Type</th>
              <th>Value</th>
              <th>Window</th>
              <th>Used</th>
              <th>Live</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {promotions.map((p) => {
              const scheduled = p.startsAt && p.startsAt > now;
              const finished = p.endsAt && p.endsAt < now;
              return (
                <tr key={p.id}>
                  <td>
                    <Link
                      href={`/admin/promotions/${p.id}`}
                      className="font-medium hover:text-brand-600"
                    >
                      {p.name}
                    </Link>
                    {p.bannerText && (
                      <span className="block truncate text-xs text-ink-500">{p.bannerText}</span>
                    )}
                  </td>
                  <td className="font-mono text-xs">{p.code ?? "auto"}</td>
                  <td className="text-xs">
                    {PROMOTION_KIND_LABELS[p.kind as PromotionKind] ?? p.kind}
                  </td>
                  <td className="text-sm">
                    {p.kind === "percent_off"
                      ? `${p.value / 100}%`
                      : p.kind === "free_shipping"
                        ? "—"
                        : formatMoney(p.value)}
                  </td>
                  <td className="text-xs text-ink-600">
                    {scheduled
                      ? `From ${p.startsAt!.toLocaleDateString("en-GB")}`
                      : finished
                        ? `Ended ${p.endsAt!.toLocaleDateString("en-GB")}`
                        : p.endsAt
                          ? `Until ${p.endsAt.toLocaleDateString("en-GB")}`
                          : "Always"}
                  </td>
                  <td className="text-sm">
                    {p.usageCount}
                    {p.usageLimit != null && ` / ${p.usageLimit}`}
                    <span className="block text-xs text-ink-400">{p._count.orders} orders</span>
                  </td>
                  <td>
                    <form action={togglePromotionAction}>
                      <input type="hidden" name="id" value={p.id} />
                      <button
                        type="submit"
                        className={`chip ${
                          p.isActive && !finished && !scheduled
                            ? "bg-emerald-100 text-emerald-800"
                            : "bg-ink-100 text-ink-600"
                        }`}
                      >
                        {p.isActive ? (scheduled ? "Scheduled" : finished ? "Expired" : "Live") : "Paused"}
                      </button>
                    </form>
                  </td>
                  <td className="text-right">
                    <Link href={`/admin/promotions/${p.id}`} className="btn-secondary btn-sm">
                      Edit
                    </Link>
                  </td>
                </tr>
              );
            })}

            {promotions.length === 0 && (
              <tr>
                <td colSpan={8} className="py-10 text-center text-ink-600">
                  No deals yet.{" "}
                  <Link href="/admin/promotions/new" className="text-brand-600 underline">
                    Create one
                  </Link>
                  .
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
