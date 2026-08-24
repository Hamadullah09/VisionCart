import Link from "next/link";
import { prisma } from "@/lib/db";
import { summariseRx } from "@/lib/rx";
import StatusChip from "@/components/shop/StatusChip";

export const metadata = { title: "Patients" };

export default async function AdminPatientsPage({ searchParams }: PageProps<"/admin/patients">) {
  const sp = await searchParams;
  const q = typeof sp.q === "string" ? sp.q.trim() : "";
  const rx = typeof sp.rx === "string" ? sp.rx : "";

  const patients = await prisma.patient.findMany({
    where: {
      deletedAt: null,
      ...(q
        ? {
            OR: [
              { fileNo: { contains: q } },
              { firstName: { contains: q } },
              { lastName: { contains: q } },
              { email: { contains: q } },
              { phone: { contains: q } },
            ],
          }
        : {}),
      ...(rx === "pending"
        ? { prescriptions: { some: { status: "pending_verification" } } }
        : {}),
    },
    include: {
      prescriptions: { orderBy: { issuedAt: "desc" }, take: 1 },
      _count: { select: { orders: true, prescriptions: true } },
    },
    orderBy: { updatedAt: "desc" },
    take: 200,
  });

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold">Patients</h1>
          <p className="text-sm text-ink-600">
            {patients.length} file{patients.length === 1 ? "" : "s"} shown. Records are kept for
            repeat dispensing — treat them as clinical data.
          </p>
        </div>
        <Link href="/admin/patients/new" className="btn-primary">
          New patient file
        </Link>
      </header>

      <form method="get" className="card flex flex-wrap gap-3 p-4">
        <input
          name="q"
          defaultValue={q}
          placeholder="File no, name, phone or email"
          className="field w-72"
        />
        <select name="rx" defaultValue={rx} className="field w-56">
          <option value="">All patients</option>
          <option value="pending">Prescription awaiting check</option>
        </select>
        <button type="submit" className="btn-secondary">
          Search
        </button>
        <Link href="/admin/patients" className="btn-secondary">
          Clear
        </Link>
      </form>

      <div className="table-wrap bg-white">
        <table className="table">
          <thead>
            <tr>
              <th>File</th>
              <th>Name</th>
              <th>Contact</th>
              <th>Latest prescription</th>
              <th>PD</th>
              <th>Orders</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {patients.map((p) => {
              const latest = p.prescriptions[0];
              return (
                <tr key={p.id}>
                  <td className="font-mono text-xs">{p.fileNo}</td>
                  <td>
                    <Link
                      href={`/admin/patients/${p.id}`}
                      className="font-medium hover:text-brand-600"
                    >
                      {p.firstName} {p.lastName}
                    </Link>
                    {p.dateOfBirth && (
                      <span className="block text-xs text-ink-500">
                        b. {p.dateOfBirth.toLocaleDateString("en-GB")}
                      </span>
                    )}
                  </td>
                  <td className="text-xs text-ink-600">
                    {p.phone ?? "—"}
                    <span className="block">{p.email ?? ""}</span>
                  </td>
                  <td>
                    {latest ? (
                      <>
                        <span className="font-mono text-xs">{summariseRx(latest)}</span>
                        <span className="mt-1 block">
                          <StatusChip status={latest.status} />
                        </span>
                      </>
                    ) : (
                      <span className="text-xs text-ink-400">None on file</span>
                    )}
                  </td>
                  <td className="text-sm">{p.pdMm ? `${p.pdMm.toFixed(1)} mm` : "—"}</td>
                  <td className="text-sm">{p._count.orders}</td>
                  <td className="text-right">
                    <Link href={`/admin/patients/${p.id}`} className="btn-secondary btn-sm">
                      Open
                    </Link>
                  </td>
                </tr>
              );
            })}

            {patients.length === 0 && (
              <tr>
                <td colSpan={7} className="py-10 text-center text-ink-600">
                  No patient files match.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
