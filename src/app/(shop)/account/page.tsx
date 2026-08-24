import Link from "next/link";
import { prisma } from "@/lib/db";
import { requireUser } from "@/lib/auth";
import { formatMoney } from "@/lib/money";
import { summariseRx } from "@/lib/rx";
import { logoutAction } from "@/app/actions/auth";
import StatusChip from "@/components/shop/StatusChip";

export const metadata = { title: "Your account" };

export default async function AccountPage() {
  const session = await requireUser();

  const patient = await prisma.patient.findUnique({
    where: { userId: session.userId },
    include: {
      prescriptions: { orderBy: { issuedAt: "desc" }, take: 3 },
      orders: {
        orderBy: { placedAt: "desc" },
        take: 3,
        include: { items: { take: 3 } },
      },
      tryOnSessions: {
        orderBy: { createdAt: "desc" },
        take: 1,
        include: { snapshots: { include: { variant: { include: { frame: true } } }, take: 6 } },
      },
    },
  });

  return (
    <div className="mx-auto max-w-5xl px-4 py-10">
      <div className="flex flex-wrap items-baseline justify-between gap-3">
        <div>
          <h1 className="text-3xl font-semibold">Hello, {session.name.split(" ")[0]}</h1>
          {patient && (
            <p className="mt-1 text-sm text-ink-600">
              Patient file <span className="font-mono">{patient.fileNo}</span>
            </p>
          )}
        </div>
        <form action={logoutAction}>
          <button type="submit" className="btn-secondary btn-sm">
            Sign out
          </button>
        </form>
      </div>

      <div className="mt-8 grid gap-6 md:grid-cols-2">
        {/* Prescriptions */}
        <section className="card p-5">
          <div className="flex items-baseline justify-between">
            <h2 className="font-semibold">Your prescriptions</h2>
            <Link href="/account/prescriptions" className="text-sm text-brand-600">
              All →
            </Link>
          </div>

          {patient?.prescriptions.length ? (
            <ul className="mt-4 space-y-3">
              {patient.prescriptions.map((rx) => (
                <li key={rx.id} className="rounded-lg border border-ink-200 p-3">
                  <div className="flex items-center justify-between">
                    <p className="text-sm font-medium">
                      {rx.issuedAt.toLocaleDateString("en-GB", {
                        day: "numeric",
                        month: "short",
                        year: "numeric",
                      })}
                    </p>
                    <StatusChip status={rx.status} />
                  </div>
                  <p className="mt-1 font-mono text-xs text-ink-600">{summariseRx(rx)}</p>
                  {rx.expiresAt && rx.expiresAt < new Date() && (
                    <p className="mt-1 text-xs text-amber-700">
                      Expired — time for a fresh eye test.
                    </p>
                  )}
                </li>
              ))}
            </ul>
          ) : (
            <p className="mt-4 text-sm text-ink-600">
              None on file yet. Add one when you order, or upload a photo of your paper
              prescription.
            </p>
          )}

          {patient?.pdMm && (
            <p className="mt-4 text-sm text-ink-600">
              Recorded PD: <span className="font-medium">{patient.pdMm.toFixed(1)} mm</span>
            </p>
          )}
        </section>

        {/* Orders */}
        <section className="card p-5">
          <div className="flex items-baseline justify-between">
            <h2 className="font-semibold">Recent orders</h2>
            <Link href="/account/orders" className="text-sm text-brand-600">
              All →
            </Link>
          </div>

          {patient?.orders.length ? (
            <ul className="mt-4 space-y-3">
              {patient.orders.map((o) => (
                <li key={o.id} className="rounded-lg border border-ink-200 p-3">
                  <div className="flex items-center justify-between">
                    <Link href={`/order/${o.orderNo}`} className="font-mono text-sm text-brand-600">
                      {o.orderNo}
                    </Link>
                    <StatusChip status={o.status} />
                  </div>
                  <p className="mt-1 text-xs text-ink-600">
                    {o.items.map((i) => i.titleSnapshot).join(", ")}
                  </p>
                  <p className="mt-1 text-sm font-medium">
                    {formatMoney(o.totalMinor, o.currency)}
                  </p>
                </li>
              ))}
            </ul>
          ) : (
            <p className="mt-4 text-sm text-ink-600">No orders yet.</p>
          )}
        </section>
      </div>

      {/* Saved try-ons */}
      {patient?.tryOnSessions[0]?.snapshots.length ? (
        <section className="card mt-6 p-5">
          <h2 className="font-semibold">Your saved try-ons</h2>
          <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
            {patient.tryOnSessions[0].snapshots.map((s) => (
              <Link
                key={s.id}
                href={`/frames/${s.variant.frame.slug}?variant=${s.variantId}`}
                className="group"
              >
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src={s.imageUrl}
                  alt={s.variant.frame.name}
                  className="aspect-4/3 w-full rounded-lg object-cover transition group-hover:opacity-90"
                />
                <p className="mt-1 truncate text-xs text-ink-600">{s.variant.frame.name}</p>
              </Link>
            ))}
          </div>
        </section>
      ) : null}

      {/* Your data */}
      <section className="card mt-6 p-5">
        <h2 className="font-semibold">Your details and data</h2>
        <dl className="mt-3 grid gap-x-8 gap-y-2 text-sm sm:grid-cols-2">
          <Detail label="Name" value={session.name} />
          <Detail label="Email" value={session.email} />
          <Detail label="Phone" value={patient?.phone ?? "—"} />
          <Detail
            label="Date of birth"
            value={patient?.dateOfBirth?.toLocaleDateString("en-GB") ?? "—"}
          />
        </dl>
        <p className="mt-4 text-xs text-ink-500">
          We keep your prescriptions and order history so repeat pairs are quick and safe to make.
          To correct or delete anything, email us and we&apos;ll action it.
        </p>
      </section>
    </div>
  );
}

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between border-b border-ink-100 py-1.5">
      <dt className="text-ink-500">{label}</dt>
      <dd className="font-medium">{value}</dd>
    </div>
  );
}
