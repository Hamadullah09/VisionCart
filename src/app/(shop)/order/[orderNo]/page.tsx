import Link from "next/link";
import { notFound } from "next/navigation";
import { prisma } from "@/lib/db";
import { getSession } from "@/lib/session";
import { formatMoney } from "@/lib/money";
import { humanise } from "@/lib/constants";

export const metadata = { title: "Order confirmed" };

export default async function OrderPage({ params, searchParams }: PageProps<"/order/[orderNo]">) {
  const { orderNo } = await params;
  const sp = await searchParams;

  const order = await prisma.order.findUnique({
    where: { orderNo },
    include: {
      items: true,
      payments: { orderBy: { createdAt: "desc" } },
      shipments: true,
      shippingAddress: true,
    },
  });
  if (!order) notFound();

  // A guest needs the order number to reach this page, which is the same level
  // of protection an emailed receipt link gives. A signed-in customer looking
  // at someone else's order is not.
  const session = await getSession();
  if (order.userId && session?.userId !== order.userId && session?.role === "customer") {
    notFound();
  }

  const payment = order.payments[0];
  const awaitingTransfer = payment?.provider === "bank_transfer" && payment.status === "pending";
  const awaitingCod = payment?.provider === "cod";

  return (
    <div className="mx-auto max-w-3xl px-4 py-14">
      <div className="text-center">
        <div className="mx-auto grid h-14 w-14 place-items-center rounded-full bg-emerald-100 text-2xl text-emerald-700">
          ✓
        </div>
        <h1 className="mt-4 text-3xl font-semibold">Thank you — order placed</h1>
        <p className="mt-2 text-ink-600">
          Order <span className="font-mono font-medium">{order.orderNo}</span>. We&apos;ve emailed a
          copy to {order.email}.
        </p>
        {sp.paid && (
          <p className="mt-2 text-sm text-emerald-700">
            Payment received — we&apos;ll start on your lenses right away.
          </p>
        )}
      </div>

      {awaitingTransfer && (
        <div className="mt-8 rounded-xl border border-amber-200 bg-amber-50 p-5">
          <h2 className="font-semibold text-amber-900">Complete your bank transfer</h2>
          <p className="mt-2 text-sm whitespace-pre-line text-amber-900">
            {process.env.BANK_TRANSFER_INSTRUCTIONS}
          </p>
          <p className="mt-3 text-sm font-medium text-amber-900">
            Amount: {formatMoney(order.totalMinor, order.currency)} · Reference: {order.orderNo}
          </p>
        </div>
      )}

      {awaitingCod && (
        <div className="mt-8 rounded-xl border border-ink-200 bg-ink-50 p-5 text-sm">
          <h2 className="font-semibold">Cash on delivery</h2>
          <p className="mt-1 text-ink-700">
            Please have {formatMoney(order.totalMinor, order.currency)} ready. The courier will call
            before arriving.
          </p>
        </div>
      )}

      <section className="card mt-8 p-5">
        <h2 className="font-semibold">What happens next</h2>
        <ol className="mt-3 space-y-3 text-sm">
          {[
            ["Prescription check", "Our optician reviews your prescription and PD."],
            ["Lens cutting", "Your lenses are surfaced, coated and glazed into the frame."],
            ["Quality check", "We verify the finished pair against your prescription."],
            ["On its way", "You get a tracking link by email and SMS."],
          ].map(([title, body], i) => (
            <li key={title} className="flex gap-3">
              <span className="grid h-6 w-6 shrink-0 place-items-center rounded-full bg-ink-100 text-xs font-semibold">
                {i + 1}
              </span>
              <span>
                <span className="block font-medium">{title}</span>
                <span className="block text-ink-600">{body}</span>
              </span>
            </li>
          ))}
        </ol>
      </section>

      <section className="card mt-6 p-5">
        <h2 className="font-semibold">Your order</h2>
        <ul className="mt-4 divide-y divide-ink-100">
          {order.items.map((item) => (
            <li key={item.id} className="flex gap-3 py-3">
              <div className="grid h-14 w-16 shrink-0 place-items-center rounded bg-ink-50 p-1">
                {item.imageSnapshot && (
                  // eslint-disable-next-line @next/next/no-img-element
                  <img
                    src={item.imageSnapshot}
                    alt=""
                    className="max-h-full max-w-full object-contain"
                  />
                )}
              </div>
              <div className="min-w-0 flex-1 text-sm">
                <p className="font-medium">{item.titleSnapshot}</p>
                <p className="text-xs text-ink-500">{item.lensSummary}</p>
                <p className="text-xs text-ink-500">
                  Qty {item.qty} · Lab: {humanise(item.labStatus)}
                </p>
              </div>
              <p className="text-sm font-medium">
                {formatMoney(item.totalMinor, order.currency)}
              </p>
            </li>
          ))}
        </ul>

        <dl className="mt-4 space-y-1.5 border-t border-ink-200 pt-4 text-sm">
          <Row label="Frames" value={formatMoney(order.subtotalMinor, order.currency)} />
          {order.lensTotalMinor > 0 && (
            <Row label="Lenses" value={formatMoney(order.lensTotalMinor, order.currency)} />
          )}
          {order.discountMinor > 0 && (
            <Row
              label={order.promoCode ? `Discount (${order.promoCode})` : "Discount"}
              value={`− ${formatMoney(order.discountMinor, order.currency)}`}
            />
          )}
          <Row
            label="Delivery"
            value={
              order.shippingMinor === 0 ? "Free" : formatMoney(order.shippingMinor, order.currency)
            }
          />
          {order.taxMinor > 0 && <Row label="Tax" value={formatMoney(order.taxMinor, order.currency)} />}
          <div className="flex justify-between border-t border-ink-200 pt-2 text-base font-semibold">
            <dt>Total</dt>
            <dd>{formatMoney(order.totalMinor, order.currency)}</dd>
          </div>
        </dl>

        {order.shippingAddress && (
          <div className="mt-5 border-t border-ink-200 pt-4 text-sm">
            <p className="font-medium">Delivering to</p>
            <p className="mt-1 text-ink-600">
              {order.shippingAddress.fullName}
              <br />
              {order.shippingAddress.line1}
              {order.shippingAddress.line2 && (
                <>
                  <br />
                  {order.shippingAddress.line2}
                </>
              )}
              <br />
              {[order.shippingAddress.city, order.shippingAddress.state, order.shippingAddress.postalCode]
                .filter(Boolean)
                .join(", ")}
            </p>
          </div>
        )}
      </section>

      <div className="mt-8 flex justify-center gap-3">
        <Link href="/account/orders" className="btn-secondary">
          Track your orders
        </Link>
        <Link href="/frames" className="btn-primary">
          Keep shopping
        </Link>
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between">
      <dt className="text-ink-600">{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}
