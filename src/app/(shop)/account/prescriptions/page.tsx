import Link from "next/link";
import { prisma } from "@/lib/db";
import { requireUser } from "@/lib/auth";
import { formatDiopter, humanise } from "@/lib/constants";
import StatusChip from "@/components/shop/StatusChip";
import RxUploader from "@/components/shop/RxUploader";

export const metadata = { title: "Your prescriptions" };

export default async function PrescriptionsPage() {
  const session = await requireUser();

  const patient = await prisma.patient.findUnique({
    where: { userId: session.userId },
    include: {
      prescriptions: { orderBy: { issuedAt: "desc" } },
      documents: { where: { kind: "prescription_scan" }, orderBy: { createdAt: "desc" } },
    },
  });

  return (
    <div className="mx-auto max-w-4xl px-4 py-10">
      <Link href="/account" className="text-sm text-brand-600">
        ← Account
      </Link>
      <h1 className="mt-2 text-3xl font-semibold">Your prescriptions</h1>
      <p className="mt-1 text-sm text-ink-600">
        Every prescription we&apos;ve worked from is kept here, so a repeat pair never needs
        re-typing.
      </p>

      <section className="card mt-8 p-5">
        <h2 className="font-semibold">Upload a paper prescription</h2>
        <p className="mt-1 text-sm text-ink-600">
          Photograph it flat in good light. Our optician reads it and adds it to your file, usually
          within a working day.
        </p>
        <div className="mt-4">
          <RxUploader />
        </div>
      </section>

      {patient?.prescriptions.length ? (
        <div className="mt-8 space-y-4">
          {patient.prescriptions.map((rx) => (
            <article key={rx.id} className="card p-5">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <h2 className="font-medium">
                    {rx.issuedAt.toLocaleDateString("en-GB", {
                      day: "numeric",
                      month: "long",
                      year: "numeric",
                    })}
                  </h2>
                  <p className="text-xs text-ink-500">
                    {[rx.prescriber, rx.clinic, humanise(rx.source)].filter(Boolean).join(" · ")}
                  </p>
                </div>
                <StatusChip status={rx.status} />
              </div>

              <div className="table-wrap mt-4">
                <table className="table min-w-[36rem]">
                  <thead>
                    <tr>
                      <th>Eye</th>
                      <th>SPH</th>
                      <th>CYL</th>
                      <th>Axis</th>
                      <th>Add</th>
                      <th>PD</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr>
                      <td className="font-medium">Right (OD)</td>
                      <td>{formatDiopter(rx.odSphere)}</td>
                      <td>{formatDiopter(rx.odCylinder)}</td>
                      <td>{rx.odAxis != null ? `${rx.odAxis}°` : "—"}</td>
                      <td>{formatDiopter(rx.odAdd)}</td>
                      <td>{rx.odPdMm ?? "—"}</td>
                    </tr>
                    <tr>
                      <td className="font-medium">Left (OS)</td>
                      <td>{formatDiopter(rx.osSphere)}</td>
                      <td>{formatDiopter(rx.osCylinder)}</td>
                      <td>{rx.osAxis != null ? `${rx.osAxis}°` : "—"}</td>
                      <td>{formatDiopter(rx.osAdd)}</td>
                      <td>{rx.osPdMm ?? "—"}</td>
                    </tr>
                  </tbody>
                </table>
              </div>

              {rx.expiresAt && (
                <p
                  className={`mt-3 text-sm ${
                    rx.expiresAt < new Date() ? "text-amber-700" : "text-ink-600"
                  }`}
                >
                  {rx.expiresAt < new Date() ? "Expired" : "Valid until"}{" "}
                  {rx.expiresAt.toLocaleDateString("en-GB")}
                </p>
              )}
              {rx.documentUrl && (
                <a
                  href={rx.documentUrl}
                  target="_blank"
                  rel="noreferrer noopener"
                  className="mt-3 inline-block text-sm text-brand-600 underline"
                >
                  View the scan you sent
                </a>
              )}
            </article>
          ))}
        </div>
      ) : (
        <p className="mt-8 text-ink-600">
          Nothing on file yet — upload one above, or type it in when you order.
        </p>
      )}

      {patient?.documents.length ? (
        <section className="mt-10">
          <h2 className="font-semibold">Uploads awaiting review</h2>
          <div className="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-4">
            {patient.documents.map((d) => (
              <a key={d.id} href={d.url} target="_blank" rel="noreferrer noopener" className="card p-2">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img src={d.url} alt={d.label ?? "Prescription scan"} className="aspect-square w-full object-cover" />
                <p className="mt-1 truncate text-xs text-ink-500">
                  {d.createdAt.toLocaleDateString("en-GB")}
                </p>
              </a>
            ))}
          </div>
        </section>
      ) : null}
    </div>
  );
}
