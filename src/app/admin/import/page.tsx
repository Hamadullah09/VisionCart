import { prisma } from "@/lib/db";
import CsvImporter from "@/components/admin/CsvImporter";
import StatusChip from "@/components/shop/StatusChip";

export const metadata = { title: "Import & export" };

const COLUMNS = {
  frames: [
    ["variant_sku", "required — unique per colourway, and the key we match on"],
    ["frame_name", "required"],
    ["price", "required — in major units, e.g. 8500"],
    ["frame_sku", "groups colourways of the same model together"],
    ["brand", "created if it doesn't exist yet"],
    ["color_name / color_hex", "the colourway and its swatch"],
    ["stock_qty", "whole number"],
    ["shape / material / rim_type / gender", "see the frame form for valid values"],
    ["lens_width_mm / bridge_width_mm / temple_length_mm / total_width_mm", "sizing"],
    ["compare_at / cost", "was-price and internal cost"],
    ["status", "draft, active or archived"],
    ["try_on_image", "URL of a transparent PNG, if you already host one"],
  ],
  patients: [
    ["first_name", "required"],
    ["file_no", "match an existing file; blank creates a new one"],
    ["last_name / email / phone", "contact details"],
    ["date_of_birth", "YYYY-MM-DD"],
    ["pd_mm", "distance pupillary distance"],
    ["od_sphere / od_cylinder / od_axis / od_add / od_pd", "right eye"],
    ["os_sphere / os_cylinder / os_axis / os_add / os_pd", "left eye"],
    ["issued / expires / prescriber", "prescription metadata"],
    ["marketing_consent", "yes or no"],
  ],
} as const;

export default async function AdminImportPage() {
  const jobs = await prisma.importJob.findMany({
    orderBy: { createdAt: "desc" },
    take: 10,
  });

  return (
    <div className="max-w-4xl space-y-8">
      <header>
        <h1 className="text-2xl font-semibold">Import &amp; export</h1>
        <p className="text-sm text-ink-600">
          Move catalogue and patient data in and out as spreadsheets. Exports are shaped so you can
          edit them and import the same file straight back.
        </p>
      </header>

      <section className="card p-5">
        <h2 className="font-semibold">Export</h2>
        <div className="mt-3 flex flex-wrap gap-2">
          {[
            ["frames", "Frames & stock"],
            ["patients", "Patients"],
            ["prescriptions", "Prescriptions"],
            ["orders", "Orders (one row per item)"],
          ].map(([type, label]) => (
            <a key={type} href={`/api/admin/export?type=${type}`} className="btn-secondary btn-sm">
              {label}
            </a>
          ))}
        </div>
        <p className="mt-3 text-xs text-ink-500">
          Exports contain patient contact details and clinical data. Handle the files accordingly —
          every download is recorded in the audit log.
        </p>
      </section>

      <section className="card p-5">
        <h2 className="font-semibold">Import</h2>
        <div className="mt-4">
          <CsvImporter />
        </div>
      </section>

      <section className="grid gap-6 lg:grid-cols-2">
        {(["frames", "patients"] as const).map((kind) => (
          <div key={kind} className="card p-5">
            <h3 className="font-semibold capitalize">{kind} columns</h3>
            <dl className="mt-3 space-y-1.5 text-xs">
              {COLUMNS[kind].map(([col, note]) => (
                <div key={col}>
                  <dt className="font-mono text-ink-800">{col}</dt>
                  <dd className="text-ink-500">{note}</dd>
                </div>
              ))}
            </dl>
          </div>
        ))}
      </section>

      {jobs.length > 0 && (
        <section>
          <h2 className="mb-3 font-semibold">Recent imports</h2>
          <div className="table-wrap bg-white">
            <table className="table">
              <thead>
                <tr>
                  <th>File</th>
                  <th>Type</th>
                  <th>When</th>
                  <th>Rows</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {jobs.map((j) => (
                  <tr key={j.id}>
                    <td className="max-w-56 truncate">{j.filename}</td>
                    <td className="capitalize">{j.kind}</td>
                    <td className="text-xs whitespace-nowrap">
                      {j.createdAt.toLocaleString("en-GB", {
                        dateStyle: "short",
                        timeStyle: "short",
                      })}
                    </td>
                    <td className="text-xs">
                      {j.okRows} ok
                      {j.errorRows > 0 && (
                        <span className="text-rose-700"> · {j.errorRows} failed</span>
                      )}
                    </td>
                    <td>
                      <StatusChip status={j.status} />
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
