import Link from "next/link";
import { prisma } from "@/lib/db";
import { formatMoney } from "@/lib/money";
import { summariseRx } from "@/lib/rx";
import StatusChip from "@/components/shop/StatusChip";

export const metadata = { title: "Dashboard" };

/** Clock reads live outside the component body so render stays pure. */
function midnightToday(): Date {
  const d = new Date();
  d.setHours(0, 0, 0, 0);
  return d;
}

function daysAgo(days: number): Date {
  return new Date(Date.now() - days * 864e5);
}

export default async function AdminDashboard() {
  const startOfToday = midnightToday();
  const thirtyDaysAgo = daysAgo(30);

  const [
    todayOrders,
    monthRevenue,
    awaitingPayment,
    inLab,
    pendingRx,
    lowStock,
    recentOrders,
    patientCount,
    activeFrames,
  ] = await Promise.all([
    prisma.order.count({ where: { placedAt: { gte: startOfToday } } }),
    prisma.order.aggregate({
      where: { paymentStatus: "paid", paidAt: { gte: thirtyDaysAgo } },
      _sum: { totalMinor: true },
    }),
    prisma.order.count({ where: { paymentStatus: "unpaid", status: { not: "cancelled" } } }),
    prisma.order.count({ where: { status: { in: ["paid", "in_lab"] } } }),
    prisma.prescription.findMany({
      where: { status: "pending_verification" },
      include: { patient: true },
      orderBy: { createdAt: "asc" },
      take: 6,
    }),
    prisma.frameVariant.findMany({
      where: { isActive: true, stockQty: { lte: 3 } },
      include: { frame: true },
      orderBy: { stockQty: "asc" },
      take: 8,
    }),
    prisma.order.findMany({
      orderBy: { placedAt: "desc" },
      take: 8,
      include: { items: { select: { titleSnapshot: true } } },
    }),
    prisma.patient.count({ where: { deletedAt: null } }),
    prisma.frame.count({ where: { status: "active" } }),
  ]);

  return (
    <div className="space-y-8">
      <header>
        <h1 className="text-2xl font-semibold">Dashboard</h1>
        <p className="text-sm text-ink-600">
          {new Date().toLocaleDateString("en-GB", {
            weekday: "long",
            day: "numeric",
            month: "long",
            year: "numeric",
          })}
        </p>
      </header>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Stat label="Orders today" value={String(todayOrders)} href="/admin/orders" />
        <Stat
          label="Paid, last 30 days"
          value={formatMoney(monthRevenue._sum.totalMinor ?? 0)}
          href="/admin/orders?paymentStatus=paid"
        />
        <Stat
          label="Awaiting payment"
          value={String(awaitingPayment)}
          href="/admin/orders?paymentStatus=unpaid"
          tone={awaitingPayment > 0 ? "warn" : undefined}
        />
        <Stat label="In the lab" value={String(inLab)} href="/admin/orders?status=in_lab" />
        <Stat label="Patient files" value={String(patientCount)} href="/admin/patients" />
        <Stat label="Live frames" value={String(activeFrames)} href="/admin/frames" />
        <Stat
          label="Prescriptions to check"
          value={String(pendingRx.length)}
          href="/admin/patients?rx=pending"
          tone={pendingRx.length > 0 ? "warn" : undefined}
        />
        <Stat
          label="Low stock lines"
          value={String(lowStock.length)}
          href="/admin/frames?stock=low"
          tone={lowStock.length > 0 ? "warn" : undefined}
        />
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        {/* Prescription queue — the thing that actually blocks orders */}
        <section className="card p-5">
          <div className="flex items-baseline justify-between">
            <h2 className="font-semibold">Prescriptions waiting for an optician</h2>
            <Link href="/admin/patients" className="text-sm text-brand-600">
              All patients →
            </Link>
          </div>

          {pendingRx.length === 0 ? (
            <p className="mt-4 text-sm text-ink-600">Nothing waiting. Good.</p>
          ) : (
            <ul className="mt-4 space-y-2">
              {pendingRx.map((rx) => (
                <li key={rx.id}>
                  <Link
                    href={`/admin/patients/${rx.patientId}`}
                    className="block rounded-lg border border-ink-200 p-3 hover:bg-ink-50"
                  >
                    <div className="flex items-center justify-between">
                      <span className="text-sm font-medium">
                        {rx.patient.firstName} {rx.patient.lastName}
                      </span>
                      <span className="font-mono text-xs text-ink-500">{rx.patient.fileNo}</span>
                    </div>
                    <p className="mt-0.5 font-mono text-xs text-ink-600">
                      {rx.documentUrl && !rx.odSphere ? "Scan uploaded — needs transcribing" : summariseRx(rx)}
                    </p>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </section>

        {/* Recent orders */}
        <section className="card p-5">
          <div className="flex items-baseline justify-between">
            <h2 className="font-semibold">Recent orders</h2>
            <Link href="/admin/orders" className="text-sm text-brand-600">
              All orders →
            </Link>
          </div>

          <ul className="mt-4 space-y-2">
            {recentOrders.map((o) => (
              <li key={o.id}>
                <Link
                  href={`/admin/orders/${o.id}`}
                  className="flex items-center justify-between gap-3 rounded-lg border border-ink-200 p-3 hover:bg-ink-50"
                >
                  <span className="min-w-0">
                    <span className="block font-mono text-sm">{o.orderNo}</span>
                    <span className="block truncate text-xs text-ink-500">
                      {o.items.map((i) => i.titleSnapshot).join(", ") || "—"}
                    </span>
                  </span>
                  <span className="flex shrink-0 items-center gap-2">
                    <StatusChip status={o.status} />
                    <span className="text-sm font-medium">
                      {formatMoney(o.totalMinor, o.currency)}
                    </span>
                  </span>
                </Link>
              </li>
            ))}
            {recentOrders.length === 0 && (
              <li className="text-sm text-ink-600">No orders yet.</li>
            )}
          </ul>
        </section>
      </div>

      {/* Low stock */}
      {lowStock.length > 0 && (
        <section className="card p-5">
          <h2 className="font-semibold">Running low</h2>
          <div className="table-wrap mt-4">
            <table className="table">
              <thead>
                <tr>
                  <th>Frame</th>
                  <th>Colour</th>
                  <th>SKU</th>
                  <th>In stock</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {lowStock.map((v) => (
                  <tr key={v.id}>
                    <td className="font-medium">{v.frame.name}</td>
                    <td>{v.colorName}</td>
                    <td className="font-mono text-xs">{v.sku}</td>
                    <td>
                      <span
                        className={`chip ${
                          v.stockQty <= 0 ? "bg-rose-100 text-rose-800" : "bg-amber-100 text-amber-800"
                        }`}
                      >
                        {v.stockQty}
                      </span>
                    </td>
                    <td className="text-right">
                      <Link href={`/admin/frames/${v.frameId}`} className="btn-secondary btn-sm">
                        Restock
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}
    </div>
  );
}

function Stat({
  label,
  value,
  href,
  tone,
}: {
  label: string;
  value: string;
  href: string;
  tone?: "warn";
}) {
  return (
    <Link
      href={href}
      className={`card p-4 transition hover:shadow-md ${tone === "warn" ? "border-amber-300 bg-amber-50" : ""}`}
    >
      <p className="text-xs tracking-wide text-ink-500 uppercase">{label}</p>
      <p className="mt-1 text-2xl font-semibold">{value}</p>
    </Link>
  );
}
