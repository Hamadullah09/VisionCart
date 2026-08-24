import Link from "next/link";
import { notFound } from "next/navigation";
import { prisma } from "@/lib/db";
import { formatMoney, fromMinor } from "@/lib/money";
import {
  CARRIERS,
  FULFILMENT_STATUSES,
  LAB_STATUSES,
  ORDER_STATUSES,
  formatDiopter,
  humanise,
} from "@/lib/constants";
import StatusChip from "@/components/shop/StatusChip";
import {
  createShipmentAction,
  recordManualPaymentAction,
  refundOrderAction,
  updateLabStatusAction,
  updateOrderAction,
} from "@/app/actions/admin";

export const metadata = { title: "Order" };

export default async function AdminOrderPage({ params }: PageProps<"/admin/orders/[id]">) {
  const { id } = await params;

  const order = await prisma.order.findUnique({
    where: { id },
    include: {
      items: { include: { prescription: true, variant: true } },
      payments: { orderBy: { createdAt: "desc" } },
      shipments: { orderBy: { createdAt: "desc" } },
      shippingAddress: true,
      patient: true,
      promotion: true,
    },
  });

  if (!order) notFound();

  const openPayment = order.payments.find((p) => p.status === "pending");
  const paidPayment = order.payments.find((p) => p.status === "succeeded");

  return (
    <div className="max-w-5xl space-y-8">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <Link href="/admin/orders" className="text-sm text-brand-600">
            ← Orders
          </Link>
          <h1 className="mt-1 font-mono text-2xl font-semibold">{order.orderNo}</h1>
          <p className="text-sm text-ink-500">
            Placed{" "}
            {order.placedAt.toLocaleString("en-GB", {
              dateStyle: "long",
              timeStyle: "short",
            })}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <StatusChip status={order.status} />
          <StatusChip status={order.paymentStatus} />
          <StatusChip status={order.fulfilmentStatus} />
        </div>
      </div>

      {/* Lab ticket — the sheet the workshop works from */}
      <section className="card p-5">
        <h2 className="font-semibold">Lab ticket</h2>
        <div className="mt-4 space-y-5">
          {order.items.map((item) => {
            const rx = item.prescription;
            return (
              <div key={item.id} className="rounded-xl border border-ink-200 p-4">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <p className="font-medium">{item.titleSnapshot}</p>
                    <p className="font-mono text-xs text-ink-500">{item.skuSnapshot}</p>
                    <p className="mt-1 text-sm text-ink-700">{item.lensSummary}</p>
                  </div>
                  <p className="font-semibold">{formatMoney(item.totalMinor, order.currency)}</p>
                </div>

                {rx ? (
                  <div className="table-wrap mt-3">
                    <table className="table min-w-[30rem]">
                      <thead>
                        <tr>
                          <th>Eye</th>
                          <th>SPH</th>
                          <th>CYL</th>
                          <th>Axis</th>
                          <th>Add</th>
                          <th>PD</th>
                          <th>Seg</th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr>
                          <td className="font-medium">OD</td>
                          <td>{formatDiopter(rx.odSphere)}</td>
                          <td>{formatDiopter(rx.odCylinder)}</td>
                          <td>{rx.odAxis ?? "—"}</td>
                          <td>{formatDiopter(rx.odAdd)}</td>
                          <td>{rx.odPdMm ?? "—"}</td>
                          <td>{rx.odSegHeightMm ?? "—"}</td>
                        </tr>
                        <tr>
                          <td className="font-medium">OS</td>
                          <td>{formatDiopter(rx.osSphere)}</td>
                          <td>{formatDiopter(rx.osCylinder)}</td>
                          <td>{rx.osAxis ?? "—"}</td>
                          <td>{formatDiopter(rx.osAdd)}</td>
                          <td>{rx.osPdMm ?? "—"}</td>
                          <td>{rx.osSegHeightMm ?? "—"}</td>
                        </tr>
                      </tbody>
                    </table>
                    <p className="border-t border-ink-100 px-3 py-2 text-xs text-ink-600">
                      Binocular PD:{" "}
                      <span className="font-medium">
                        {order.patient?.pdMm ? `${order.patient.pdMm.toFixed(1)} mm` : "not recorded"}
                      </span>
                      {order.patient?.pdNearMm && ` · near ${order.patient.pdNearMm.toFixed(1)} mm`}
                    </p>
                  </div>
                ) : (
                  <p className="mt-3 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">
                    No prescription attached — the customer is sending one. Don&apos;t start this
                    line yet.
                  </p>
                )}

                {rx && rx.status !== "verified" && (
                  <p className="mt-2 text-sm text-amber-700">
                    Prescription is {humanise(rx.status).toLowerCase()} — verify it on the{" "}
                    <Link
                      href={`/admin/patients/${order.patientId}`}
                      className="underline"
                    >
                      patient file
                    </Link>{" "}
                    before cutting.
                  </p>
                )}

                <form action={updateLabStatusAction} className="mt-3 flex flex-wrap items-end gap-2">
                  <input type="hidden" name="itemId" value={item.id} />
                  <input type="hidden" name="orderId" value={order.id} />
                  <div>
                    <label className="label" htmlFor={`lab-${item.id}`}>
                      Lab stage
                    </label>
                    <select
                      id={`lab-${item.id}`}
                      name="labStatus"
                      defaultValue={item.labStatus}
                      className="field w-44"
                    >
                      {LAB_STATUSES.map((s) => (
                        <option key={s} value={s}>
                          {humanise(s)}
                        </option>
                      ))}
                    </select>
                  </div>
                  <div>
                    <label className="label" htmlFor={`labref-${item.id}`}>
                      Lab reference
                    </label>
                    <input
                      id={`labref-${item.id}`}
                      name="labRef"
                      defaultValue={item.labRef ?? ""}
                      className="field w-44"
                    />
                  </div>
                  <button type="submit" className="btn-secondary btn-sm">
                    Update
                  </button>
                </form>
              </div>
            );
          })}
        </div>
      </section>

      <div className="grid gap-6 lg:grid-cols-2">
        {/* Totals & customer */}
        <section className="card p-5">
          <h2 className="font-semibold">Totals</h2>
          <dl className="mt-3 space-y-1.5 text-sm">
            <Row label="Frames" value={formatMoney(order.subtotalMinor, order.currency)} />
            <Row label="Lenses" value={formatMoney(order.lensTotalMinor, order.currency)} />
            {order.discountMinor > 0 && (
              <Row
                label={`Discount ${order.promoCode ? `(${order.promoCode})` : ""}`}
                value={`− ${formatMoney(order.discountMinor, order.currency)}`}
              />
            )}
            <Row label="Delivery" value={formatMoney(order.shippingMinor, order.currency)} />
            {order.taxMinor > 0 && (
              <Row label="Tax" value={formatMoney(order.taxMinor, order.currency)} />
            )}
            <div className="flex justify-between border-t border-ink-200 pt-2 font-semibold">
              <dt>Total</dt>
              <dd>{formatMoney(order.totalMinor, order.currency)}</dd>
            </div>
          </dl>

          <h3 className="mt-5 font-semibold">Customer</h3>
          <p className="mt-1 text-sm text-ink-700">
            {order.email}
            <br />
            {order.phone}
          </p>
          {order.patient && (
            <Link
              href={`/admin/patients/${order.patient.id}`}
              className="mt-2 inline-block text-sm text-brand-600 underline"
            >
              Open patient file {order.patient.fileNo}
            </Link>
          )}

          {order.shippingAddress && (
            <>
              <h3 className="mt-5 font-semibold">Delivery address</h3>
              <p className="mt-1 text-sm whitespace-pre-line text-ink-700">
                {[
                  order.shippingAddress.fullName,
                  order.shippingAddress.line1,
                  order.shippingAddress.line2,
                  [
                    order.shippingAddress.city,
                    order.shippingAddress.state,
                    order.shippingAddress.postalCode,
                  ]
                    .filter(Boolean)
                    .join(", "),
                  order.shippingAddress.country,
                ]
                  .filter(Boolean)
                  .join("\n")}
              </p>
            </>
          )}

          {order.notes && (
            <>
              <h3 className="mt-5 font-semibold">Customer note</h3>
              <p className="mt-1 text-sm text-ink-700">{order.notes}</p>
            </>
          )}
        </section>

        {/* Status & payment actions */}
        <section className="space-y-6">
          <div className="card p-5">
            <h2 className="font-semibold">Move this order along</h2>
            <form action={updateOrderAction} className="mt-3 space-y-3">
              <input type="hidden" name="id" value={order.id} />
              <div className="grid gap-3 sm:grid-cols-2">
                <div>
                  <label className="label" htmlFor="status">
                    Order status
                  </label>
                  <select id="status" name="status" defaultValue={order.status} className="field">
                    {ORDER_STATUSES.map((s) => (
                      <option key={s} value={s}>
                        {humanise(s)}
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="label" htmlFor="fulfilmentStatus">
                    Fulfilment
                  </label>
                  <select
                    id="fulfilmentStatus"
                    name="fulfilmentStatus"
                    defaultValue={order.fulfilmentStatus}
                    className="field"
                  >
                    {FULFILMENT_STATUSES.map((s) => (
                      <option key={s} value={s}>
                        {humanise(s)}
                      </option>
                    ))}
                  </select>
                </div>
              </div>
              <div>
                <label className="label" htmlFor="internalNotes">
                  Internal notes
                </label>
                <textarea
                  id="internalNotes"
                  name="internalNotes"
                  rows={2}
                  defaultValue={order.internalNotes ?? ""}
                  className="field"
                />
              </div>
              <button type="submit" className="btn-primary btn-sm">
                Save
              </button>
              <p className="text-xs text-ink-500">
                Cancelling returns the frames to stock automatically.
              </p>
            </form>
          </div>

          <div className="card p-5">
            <h2 className="font-semibold">Payment</h2>
            {order.payments.length === 0 ? (
              <p className="mt-2 text-sm text-ink-600">No payment recorded.</p>
            ) : (
              <ul className="mt-3 space-y-2 text-sm">
                {order.payments.map((p) => (
                  <li
                    key={p.id}
                    className="flex items-center justify-between rounded-lg border border-ink-200 px-3 py-2"
                  >
                    <span>
                      {humanise(p.provider)}
                      <span className="block font-mono text-xs text-ink-400">
                        {p.providerRef ?? "—"}
                      </span>
                    </span>
                    <span className="flex items-center gap-2">
                      <StatusChip status={p.status} />
                      {formatMoney(p.amountMinor, p.currency)}
                    </span>
                  </li>
                ))}
              </ul>
            )}

            {openPayment && order.paymentStatus !== "paid" && (
              <form action={recordManualPaymentAction} className="mt-4 flex flex-wrap items-end gap-2">
                <input type="hidden" name="orderId" value={order.id} />
                <div className="flex-1">
                  <label className="label" htmlFor="reference">
                    Payment reference
                  </label>
                  <input
                    id="reference"
                    name="reference"
                    placeholder="Transfer ID or receipt no"
                    className="field"
                  />
                </div>
                <button type="submit" className="btn-primary btn-sm">
                  Mark as paid
                </button>
              </form>
            )}

            {paidPayment && (
              <form action={refundOrderAction} className="mt-4 flex flex-wrap items-end gap-2">
                <input type="hidden" name="orderId" value={order.id} />
                <input type="hidden" name="paymentId" value={paidPayment.id} />
                <div className="flex-1">
                  <label className="label" htmlFor="amount">
                    Refund amount
                  </label>
                  <input
                    id="amount"
                    name="amount"
                    type="number"
                    step="0.01"
                    placeholder={String(fromMinor(paidPayment.amountMinor))}
                    className="field"
                  />
                </div>
                <button type="submit" className="btn-danger btn-sm">
                  Refund
                </button>
                <p className="w-full text-xs text-ink-500">
                  Leave blank for a full refund. Card refunds go back through Stripe; cash and
                  transfer refunds are recorded here and paid out by hand.
                </p>
              </form>
            )}
          </div>

          <div className="card p-5">
            <h2 className="font-semibold">Shipping</h2>
            {order.shipments.length > 0 && (
              <ul className="mt-3 space-y-2 text-sm">
                {order.shipments.map((s) => (
                  <li key={s.id} className="rounded-lg border border-ink-200 px-3 py-2">
                    <div className="flex items-center justify-between">
                      <span className="font-medium">{s.carrier.toUpperCase()}</span>
                      <StatusChip status={s.status} />
                    </div>
                    {s.trackingNumber && (
                      <p className="font-mono text-xs text-ink-600">{s.trackingNumber}</p>
                    )}
                    <div className="mt-1 flex gap-3 text-xs">
                      {s.trackingUrl && (
                        <a
                          href={s.trackingUrl}
                          target="_blank"
                          rel="noreferrer noopener"
                          className="text-brand-600 underline"
                        >
                          Track
                        </a>
                      )}
                      {s.labelUrl && (
                        <a
                          href={s.labelUrl}
                          target="_blank"
                          rel="noreferrer noopener"
                          className="text-brand-600 underline"
                        >
                          Print label
                        </a>
                      )}
                    </div>
                  </li>
                ))}
              </ul>
            )}

            <form action={createShipmentAction} className="mt-4 space-y-3">
              <input type="hidden" name="orderId" value={order.id} />
              <div className="grid gap-3 sm:grid-cols-2">
                <div>
                  <label className="label" htmlFor="carrier">
                    Carrier
                  </label>
                  <select id="carrier" name="carrier" className="field">
                    {CARRIERS.map((c) => (
                      <option key={c} value={c}>
                        {c.toUpperCase()}
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="label" htmlFor="trackingNumber">
                    Tracking number
                  </label>
                  <input id="trackingNumber" name="trackingNumber" className="field" />
                </div>
                <div className="sm:col-span-2">
                  <label className="label" htmlFor="trackingUrl">
                    Tracking URL
                  </label>
                  <input id="trackingUrl" name="trackingUrl" className="field" />
                </div>
              </div>
              <button type="submit" className="btn-primary btn-sm">
                Mark as shipped
              </button>
              <p className="text-xs text-ink-500">
                With Shippo configured this buys a real label; otherwise it records the courier
                details you type here.
              </p>
            </form>
          </div>
        </section>
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
