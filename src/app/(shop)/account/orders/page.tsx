import Link from "next/link";
import { prisma } from "@/lib/db";
import { requireUser } from "@/lib/auth";
import { formatMoney } from "@/lib/money";
import StatusChip from "@/components/shop/StatusChip";

export const metadata = { title: "Your orders" };

export default async function AccountOrdersPage() {
  const session = await requireUser();

  const orders = await prisma.order.findMany({
    where: { userId: session.userId },
    orderBy: { placedAt: "desc" },
    include: { items: true, shipments: true },
  });

  return (
    <div className="mx-auto max-w-4xl px-4 py-10">
      <Link href="/account" className="text-sm text-brand-600">
        ← Account
      </Link>
      <h1 className="mt-2 text-3xl font-semibold">Your orders</h1>

      {orders.length === 0 ? (
        <p className="mt-8 text-ink-600">
          Nothing here yet.{" "}
          <Link href="/frames" className="text-brand-600 underline">
            Browse frames
          </Link>
          .
        </p>
      ) : (
        <div className="mt-8 space-y-4">
          {orders.map((o) => {
            const shipment = o.shipments[0];
            return (
              <article key={o.id} className="card p-5">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div>
                    <Link href={`/order/${o.orderNo}`} className="font-mono font-medium text-brand-600">
                      {o.orderNo}
                    </Link>
                    <p className="text-xs text-ink-500">
                      Placed {o.placedAt.toLocaleDateString("en-GB", {
                        day: "numeric",
                        month: "long",
                        year: "numeric",
                      })}
                    </p>
                  </div>
                  <div className="flex items-center gap-2">
                    <StatusChip status={o.status} />
                    <StatusChip status={o.paymentStatus} />
                  </div>
                </div>

                <ul className="mt-4 space-y-2 text-sm">
                  {o.items.map((i) => (
                    <li key={i.id} className="flex justify-between gap-4">
                      <span>
                        {i.titleSnapshot}
                        <span className="block text-xs text-ink-500">{i.lensSummary}</span>
                      </span>
                      <span className="shrink-0">{formatMoney(i.totalMinor, o.currency)}</span>
                    </li>
                  ))}
                </ul>

                <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-ink-100 pt-3">
                  <p className="font-semibold">{formatMoney(o.totalMinor, o.currency)}</p>
                  {shipment?.trackingUrl ? (
                    <a
                      href={shipment.trackingUrl}
                      target="_blank"
                      rel="noreferrer noopener"
                      className="btn-secondary btn-sm"
                    >
                      Track parcel
                    </a>
                  ) : shipment?.trackingNumber ? (
                    <p className="text-sm text-ink-600">
                      {shipment.carrier.toUpperCase()} · {shipment.trackingNumber}
                    </p>
                  ) : null}
                </div>
              </article>
            );
          })}
        </div>
      )}
    </div>
  );
}
