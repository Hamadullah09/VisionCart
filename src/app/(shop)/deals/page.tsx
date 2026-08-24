import Link from "next/link";
import { prisma } from "@/lib/db";
import { describe } from "@/lib/promotions";
import { formatMoney } from "@/lib/money";

export const metadata = {
  title: "Deals",
  description: "Current offers on frames and lenses.",
};

export default async function DealsPage() {
  const now = new Date();
  const promos = await prisma.promotion.findMany({
    where: {
      isActive: true,
      AND: [
        { OR: [{ startsAt: null }, { startsAt: { lte: now } }] },
        { OR: [{ endsAt: null }, { endsAt: { gte: now } }] },
      ],
    },
    orderBy: [{ priority: "desc" }, { createdAt: "desc" }],
  });

  return (
    <div className="mx-auto max-w-5xl px-4 py-10">
      <h1 className="text-3xl font-semibold">Deals on now</h1>
      <p className="mt-1 text-ink-600">
        Codes apply at checkout. Unless a deal says otherwise, only one applies per order.
      </p>

      {promos.length === 0 ? (
        <p className="mt-10 text-ink-600">
          Nothing running right now.{" "}
          <Link href="/frames" className="text-brand-600 underline">
            Browse the range
          </Link>{" "}
          — our everyday prices already include a hard coat and anti-scratch.
        </p>
      ) : (
        <div className="mt-8 grid gap-5 sm:grid-cols-2">
          {promos.map((p) => (
            <article
              key={p.id}
              className="overflow-hidden rounded-2xl border border-ink-200"
            >
              <div
                className="p-6 text-white"
                style={{ background: p.bannerColor || "#0a67a1" }}
              >
                <h2 className="text-xl font-semibold">{p.name}</h2>
                <p className="mt-1 text-white/85">{p.description || describe(p)}</p>
              </div>

              <div className="space-y-2 p-5 text-sm">
                {p.code ? (
                  <p>
                    Use code{" "}
                    <span className="rounded bg-ink-100 px-2 py-0.5 font-mono tracking-wider">
                      {p.code}
                    </span>
                  </p>
                ) : (
                  <p className="text-emerald-700">Applied automatically at checkout.</p>
                )}

                {p.minSubtotalMinor > 0 && (
                  <p className="text-ink-600">
                    On orders over {formatMoney(p.minSubtotalMinor)}
                  </p>
                )}
                {p.minQty > 1 && <p className="text-ink-600">Minimum {p.minQty} items</p>}
                {p.firstOrderOnly && <p className="text-ink-600">First order only</p>}
                {p.usageLimit != null && (
                  <p className="text-ink-600">
                    {Math.max(0, p.usageLimit - p.usageCount)} of {p.usageLimit} left
                  </p>
                )}
                {p.endsAt && (
                  <p className="font-medium text-ink-800">
                    Ends{" "}
                    {p.endsAt.toLocaleDateString("en-GB", {
                      day: "numeric",
                      month: "long",
                    })}
                  </p>
                )}

                <Link href="/frames" className="btn-primary btn-sm mt-3">
                  Shop frames
                </Link>
              </div>
            </article>
          ))}
        </div>
      )}
    </div>
  );
}
