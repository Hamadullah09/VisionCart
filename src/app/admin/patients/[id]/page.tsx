import Link from "next/link";
import { notFound } from "next/navigation";
import { prisma } from "@/lib/db";
import { formatMoney } from "@/lib/money";
import { formatDiopter, humanise } from "@/lib/constants";
import { summariseRx, recommendedIndex } from "@/lib/rx";
import StatusChip from "@/components/shop/StatusChip";
import PatientForm from "@/components/admin/PatientForm";
import PrescriptionForm from "@/components/admin/PrescriptionForm";
import { verifyPrescriptionAction } from "@/app/actions/admin";

export const metadata = { title: "Patient file" };

export default async function PatientPage({ params, searchParams }: PageProps<"/admin/patients/[id]">) {
  const { id } = await params;
  const sp = await searchParams;
  const editRxId = typeof sp.rx === "string" ? sp.rx : null;

  const patient = await prisma.patient.findUnique({
    where: { id },
    include: {
      user: { select: { email: true, lastLoginAt: true } },
      prescriptions: { orderBy: { issuedAt: "desc" } },
      documents: { orderBy: { createdAt: "desc" } },
      appointments: { orderBy: { startsAt: "desc" }, take: 5 },
      orders: {
        orderBy: { placedAt: "desc" },
        include: { items: { select: { titleSnapshot: true, lensSummary: true } } },
      },
      tryOnSessions: {
        orderBy: { createdAt: "desc" },
        take: 3,
        include: { snapshots: { include: { variant: { include: { frame: true } } } } },
      },
    },
  });

  if (!patient) notFound();

  const editing = editRxId ? patient.prescriptions.find((r) => r.id === editRxId) : null;
  const latest = patient.prescriptions[0];

  return (
    <div className="max-w-5xl space-y-8">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <Link href="/admin/patients" className="text-sm text-brand-600">
            ← Patients
          </Link>
          <h1 className="mt-1 text-2xl font-semibold">
            {patient.firstName} {patient.lastName}
          </h1>
          <p className="text-sm text-ink-500">
            File <span className="font-mono">{patient.fileNo}</span>
            {patient.user && " · has a shop login"}
            {patient.dateOfBirth &&
              ` · born ${patient.dateOfBirth.toLocaleDateString("en-GB")}`}
          </p>
        </div>
        <div className="text-right text-sm">
          <p className="text-ink-600">{patient.orders.length} orders</p>
          <p className="text-ink-600">
            {patient.pdMm ? `PD ${patient.pdMm.toFixed(1)} mm` : "PD not recorded"}
          </p>
        </div>
      </div>

      {latest && (
        <div className="card p-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 className="font-semibold">Current prescription</h2>
              <p className="font-mono text-sm text-ink-700">{summariseRx(latest)}</p>
              <p className="mt-1 text-xs text-ink-500">
                Issued {latest.issuedAt.toLocaleDateString("en-GB")}
                {latest.expiresAt && ` · expires ${latest.expiresAt.toLocaleDateString("en-GB")}`}
                {latest.verifiedBy && ` · checked by ${latest.verifiedBy}`}
              </p>
              <p className="mt-1 text-xs text-ink-500">
                Suggested lens index: {recommendedIndex(latest)}
              </p>
            </div>
            <StatusChip status={latest.status} />
          </div>
        </div>
      )}

      <section>
        <h2 className="mb-3 text-lg font-semibold">Patient details</h2>
        <PatientForm patient={patient} />
      </section>

      {/* Prescriptions */}
      <section className="space-y-4">
        <h2 className="text-lg font-semibold">Prescriptions</h2>

        {patient.prescriptions.length > 0 && (
          <div className="table-wrap bg-white">
            <table className="table">
              <thead>
                <tr>
                  <th>Issued</th>
                  <th>OD</th>
                  <th>OS</th>
                  <th>Source</th>
                  <th>Status</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {patient.prescriptions.map((rx) => (
                  <tr key={rx.id}>
                    <td className="whitespace-nowrap">
                      {rx.issuedAt.toLocaleDateString("en-GB")}
                    </td>
                    <td className="font-mono text-xs">
                      {formatDiopter(rx.odSphere)} / {formatDiopter(rx.odCylinder)} ×{" "}
                      {rx.odAxis ?? "—"}
                    </td>
                    <td className="font-mono text-xs">
                      {formatDiopter(rx.osSphere)} / {formatDiopter(rx.osCylinder)} ×{" "}
                      {rx.osAxis ?? "—"}
                    </td>
                    <td className="text-xs">{humanise(rx.source)}</td>
                    <td>
                      <StatusChip status={rx.status} />
                    </td>
                    <td className="text-right">
                      <div className="flex justify-end gap-1">
                        {rx.documentUrl && (
                          <a
                            href={rx.documentUrl}
                            target="_blank"
                            rel="noreferrer noopener"
                            className="btn-secondary btn-sm"
                          >
                            Scan
                          </a>
                        )}
                        <Link
                          href={`/admin/patients/${patient.id}?rx=${rx.id}`}
                          className="btn-secondary btn-sm"
                        >
                          Edit
                        </Link>
                        {rx.status === "pending_verification" && (
                          <>
                            <form action={verifyPrescriptionAction}>
                              <input type="hidden" name="id" value={rx.id} />
                              <input type="hidden" name="decision" value="verify" />
                              <button type="submit" className="btn-primary btn-sm">
                                Verify
                              </button>
                            </form>
                            <form action={verifyPrescriptionAction}>
                              <input type="hidden" name="id" value={rx.id} />
                              <input type="hidden" name="decision" value="reject" />
                              <button type="submit" className="btn-danger btn-sm">
                                Reject
                              </button>
                            </form>
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <div className="card p-5">
          <h3 className="mb-3 font-semibold">
            {editing ? "Edit prescription" : "Add a prescription"}
          </h3>
          <PrescriptionForm
            key={editing?.id ?? "new"}
            patientId={patient.id}
            prescription={editing}
            defaultPdMm={patient.pdMm}
            defaultPdNearMm={patient.pdNearMm}
          />
          {editing && (
            <Link
              href={`/admin/patients/${patient.id}`}
              className="mt-3 inline-block text-sm text-ink-500 underline"
            >
              Cancel edit
            </Link>
          )}
        </div>
      </section>

      {/* Orders */}
      <section>
        <h2 className="mb-3 text-lg font-semibold">Order history</h2>
        {patient.orders.length === 0 ? (
          <p className="text-sm text-ink-600">No orders on this file yet.</p>
        ) : (
          <div className="table-wrap bg-white">
            <table className="table">
              <thead>
                <tr>
                  <th>Order</th>
                  <th>Placed</th>
                  <th>Items</th>
                  <th>Total</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {patient.orders.map((o) => (
                  <tr key={o.id}>
                    <td>
                      <Link href={`/admin/orders/${o.id}`} className="font-mono text-brand-600">
                        {o.orderNo}
                      </Link>
                    </td>
                    <td className="whitespace-nowrap">{o.placedAt.toLocaleDateString("en-GB")}</td>
                    <td className="text-xs">
                      {o.items.map((i) => i.titleSnapshot).join(", ")}
                    </td>
                    <td>{formatMoney(o.totalMinor, o.currency)}</td>
                    <td>
                      <StatusChip status={o.status} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {/* Documents & try-ons */}
      <div className="grid gap-6 lg:grid-cols-2">
        <section>
          <h2 className="mb-3 text-lg font-semibold">Documents</h2>
          {patient.documents.length === 0 ? (
            <p className="text-sm text-ink-600">Nothing uploaded.</p>
          ) : (
            <div className="grid grid-cols-3 gap-2">
              {patient.documents.map((d) => (
                <a
                  key={d.id}
                  href={d.url}
                  target="_blank"
                  rel="noreferrer noopener"
                  className="card p-2"
                >
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img
                    src={d.url}
                    alt={d.label ?? d.kind}
                    className="aspect-square w-full bg-ink-50 object-cover"
                  />
                  <p className="mt-1 truncate text-[11px] text-ink-500">{humanise(d.kind)}</p>
                </a>
              ))}
            </div>
          )}
        </section>

        <section>
          <h2 className="mb-3 text-lg font-semibold">Try-on snapshots</h2>
          {patient.tryOnSessions.flatMap((s) => s.snapshots).length === 0 ? (
            <p className="text-sm text-ink-600">None saved.</p>
          ) : (
            <div className="grid grid-cols-3 gap-2">
              {patient.tryOnSessions
                .flatMap((s) => s.snapshots)
                .slice(0, 9)
                .map((s) => (
                  <a
                    key={s.id}
                    href={s.imageUrl}
                    target="_blank"
                    rel="noreferrer noopener"
                    className="card p-2"
                  >
                    {/* eslint-disable-next-line @next/next/no-img-element */}
                    <img
                      src={s.imageUrl}
                      alt={s.variant.frame.name}
                      className="aspect-square w-full object-cover"
                    />
                    <p className="mt-1 truncate text-[11px] text-ink-500">
                      {s.variant.frame.name}
                    </p>
                  </a>
                ))}
            </div>
          )}
          {patient.faceMetrics && (
            <p className="mt-3 text-xs text-ink-500">
              Measured from a try-on photo. Confirm before ordering lenses.
            </p>
          )}
        </section>
      </div>
    </div>
  );
}
