import Link from "next/link";
import { prisma } from "@/lib/db";
import { formatMoney } from "@/lib/money";
import { ORDER_STATUSES, PAYMENT_STATUSES, humanise } from "@/lib/constants";
import StatusChip from "@/components/shop/StatusChip";

export const metadata = { title: "Orders" };

export default async function AdminOrdersPage({ searchParams }: PageProps<"/admin/orders">) {
  const sp = await searchParams;
  const q = typeof sp.q === "string" ? sp.q.trim() : "";
  const status = typeof sp.status === "string" ? sp.status : "";
  const paymentStatus = typeof sp.paymentStatus === "string" ? sp.paymentStatus : "";

  const orders = await prisma.order.findMany({
    where: {
      ...(q
        ? { OR: [{ orderNo: { contains: q } }, { email: { contains: q } }, { phone: { contains: q } }] }
        : {}),
      ...(status ? { status } : {}),
      ...(paymentStatus ? { paymentStatus } : {}),
    },
    include: {
      items: { select: { titleSnapshot: true, labStatus: true } },
      patient: { select: { id: true, fileNo: true, firstName: true, lastName: true } },
    },
    orderBy: { placedAt: "desc" },
    take: 200,
  });

  const revenue = orders
    .filter((o) => o.paymentStatus === "paid")
    .reduce((s, o) => s + o.totalMinor, 0);

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-semibold">Orders</h1>
        <p className="text-sm text-ink-600">
          {orders.length} shown · {formatMoney(revenue)} paid
        </p>
      </header>

      <form method="get" className="card flex flex-wrap gap-3 p-4">
        <input
          name="q"
          defaultValue={q}
          placeholder="Order no, email or phone"
          className="field w-64"
        />
        <select name="status" defaultValue={status} className="field w-44">
          <option value="">Any status</option>
          {ORDER_STATUSES.map((s) => (
            <option key={s} value={s}>
              {humanise(s)}
            </option>
          ))}
        </select>
        <select name="paymentStatus" defaultValue={paymentStatus} className="field w-44">
          <option value="">Any payment</option>
          {PAYMENT_STATUSES.map((s) => (
            <option key={s} value={s}>
              {humanise(s)}
            </option>
          ))}
        </select>
        <button type="submit" className="btn-secondary">
          Filter
        </button>
        <Link href="/admin/orders" className="btn-secondary">
          Clear
        </Link>
      </form>

      <div className="table-wrap bg-white">
        <table className="table">
          <thead>
            <tr>
              <th>Order</th>
              <th>Placed</th>
              <th>Customer</th>
              <th>Items</th>
              <th>Total</th>
              <th>Payment</th>
              <th>Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {orders.map((o) => (
              <tr key={o.id}>
                <td>
                  <Link href={`/admin/orders/${o.id}`} className="font-mono text-brand-600">
                    {o.orderNo}
                  </Link>
                </td>
                <td className="whitespace-nowrap text-xs">
                  {o.placedAt.toLocaleDateString("en-GB")}
                  <span className="block text-ink-400">
                    {o.placedAt.toLocaleTimeString("en-GB", {
                      hour: "2-digit",
                      minute: "2-digit",
                    })}
                  </span>
                </td>
                <td className="text-xs">
                  {o.patient ? (
                    <Link href={`/admin/patients/${o.patient.id}`} className="hover:text-brand-600">
                      {o.patient.firstName} {o.patient.lastName}
                      <span className="block font-mono text-ink-400">{o.patient.fileNo}</span>
                    </Link>
                  ) : (
                    o.email
                  )}
                </td>
                <td className="max-w-56 truncate text-xs">
                  {o.items.map((i) => i.titleSnapshot).join(", ")}
                </td>
                <td className="whitespace-nowrap">{formatMoney(o.totalMinor, o.currency)}</td>
                <td>
                  <StatusChip status={o.paymentStatus} />
                </td>
                <td>
                  <StatusChip status={o.status} />
                </td>
                <td className="text-right">
                  <Link href={`/admin/orders/${o.id}`} className="btn-secondary btn-sm">
                    Open
                  </Link>
                </td>
              </tr>
            ))}

            {orders.length === 0 && (
              <tr>
                <td colSpan={8} className="py-10 text-center text-ink-600">
                  No orders match.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
