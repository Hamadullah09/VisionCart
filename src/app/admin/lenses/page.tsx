import Link from "next/link";
import { prisma } from "@/lib/db";
import { formatMoney, fromMinor } from "@/lib/money";
import { LENS_GROUPS, LENS_GROUP_LABELS, humanise, type LensGroup } from "@/lib/constants";
import { saveLensOptionAction, deleteLensOptionAction } from "@/app/actions/admin";

export const metadata = { title: "Lenses" };

export default async function AdminLensesPage({ searchParams }: PageProps<"/admin/lenses">) {
  const sp = await searchParams;
  const editId = typeof sp.id === "string" ? sp.id : null;

  const options = await prisma.lensOption.findMany({
    orderBy: [{ group: "asc" }, { position: "asc" }],
  });
  const editing = editId ? options.find((o) => o.id === editId) : null;

  return (
    <div className="max-w-5xl space-y-8">
      <header>
        <h1 className="text-2xl font-semibold">Lens options &amp; pricing</h1>
        <p className="text-sm text-ink-600">
          These are the steps a customer walks through when choosing lenses. Change a price here and
          the storefront follows immediately.
        </p>
      </header>

      {LENS_GROUPS.map((group) => {
        const rows = options.filter((o) => o.group === group);
        if (rows.length === 0) return null;

        return (
          <section key={group}>
            <h2 className="mb-2 font-semibold">{LENS_GROUP_LABELS[group as LensGroup]}</h2>
            <div className="table-wrap bg-white">
              <table className="table">
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Code</th>
                    <th>Price</th>
                    <th>Rx limits</th>
                    <th>Default</th>
                    <th>Live</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((o) => (
                    <tr key={o.id}>
                      <td>
                        <span className="font-medium">{o.name}</span>
                        {o.description && (
                          <span className="block text-xs text-ink-500">{o.description}</span>
                        )}
                      </td>
                      <td className="font-mono text-xs">{o.code}</td>
                      <td>{o.priceMinor === 0 ? "Included" : formatMoney(o.priceMinor)}</td>
                      <td className="text-xs text-ink-600">
                        {o.maxSphere != null && `≤ ${Math.abs(o.maxSphere).toFixed(2)} D sph`}
                        {o.maxCylinder != null && (
                          <span className="block">≤ {Math.abs(o.maxCylinder).toFixed(2)} D cyl</span>
                        )}
                        {o.maxSphere == null && o.maxCylinder == null && "—"}
                      </td>
                      <td>{o.isDefault ? "✓" : ""}</td>
                      <td>{o.isActive ? "✓" : "—"}</td>
                      <td className="text-right">
                        <div className="flex justify-end gap-1">
                          <Link href={`/admin/lenses?id=${o.id}`} className="btn-secondary btn-sm">
                            Edit
                          </Link>
                          {o.isActive && (
                            <form action={deleteLensOptionAction}>
                              <input type="hidden" name="id" value={o.id} />
                              <button type="submit" className="btn-danger btn-sm">
                                Retire
                              </button>
                            </form>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        );
      })}

      <section className="card p-5">
        <h2 className="font-semibold">{editing ? `Edit "${editing.name}"` : "Add a lens option"}</h2>

        <form action={saveLensOptionAction} className="mt-4 space-y-4">
          {editing && <input type="hidden" name="id" value={editing.id} />}

          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <div>
              <label className="label" htmlFor="group">
                Step
              </label>
              <select
                id="group"
                name="group"
                defaultValue={editing?.group ?? "coating"}
                className="field"
              >
                {LENS_GROUPS.map((g) => (
                  <option key={g} value={g}>
                    {humanise(g)}
                  </option>
                ))}
              </select>
            </div>
            <Text name="name" label="Name" defaultValue={editing?.name} required />
            <Text
              name="code"
              label="Code"
              defaultValue={editing?.code}
              required
              hint="Stable identifier, e.g. idx-167."
            />
            <Text
              name="price"
              label="Extra cost"
              type="number"
              step="0.01"
              defaultValue={editing ? String(fromMinor(editing.priceMinor)) : "0"}
            />
            <Text
              name="maxSphere"
              label="Max sphere (D)"
              type="number"
              step="0.25"
              defaultValue={editing?.maxSphere?.toString() ?? ""}
              hint="Blocks the option above this strength."
            />
            <Text
              name="maxCylinder"
              label="Max cylinder (D)"
              type="number"
              step="0.25"
              defaultValue={editing?.maxCylinder?.toString() ?? ""}
            />
            <Text
              name="requires"
              label="Requires"
              defaultValue={editing?.requires ?? ""}
              hint="Comma-separated codes."
            />
            <Text
              name="excludes"
              label="Conflicts with"
              defaultValue={editing?.excludes ?? ""}
              hint="Comma-separated codes."
            />
            <Text
              name="position"
              label="Sort order"
              type="number"
              defaultValue={String(editing?.position ?? 0)}
            />
          </div>

          <div>
            <label className="label" htmlFor="description">
              Description
            </label>
            <input
              id="description"
              name="description"
              defaultValue={editing?.description ?? ""}
              className="field"
              placeholder="One line the customer will read when choosing."
            />
          </div>

          <div className="flex flex-wrap gap-4 text-sm">
            <label className="flex items-center gap-2">
              <input type="checkbox" name="isDefault" defaultChecked={editing?.isDefault} />
              Pre-selected for this step
            </label>
            <label className="flex items-center gap-2">
              <input type="checkbox" name="isActive" defaultChecked={editing?.isActive ?? true} />
              Offer it in the shop
            </label>
          </div>

          <div className="flex gap-2">
            <button type="submit" className="btn-primary">
              {editing ? "Save option" : "Add option"}
            </button>
            {editing && (
              <Link href="/admin/lenses" className="btn-secondary">
                Cancel
              </Link>
            )}
          </div>
        </form>
      </section>
    </div>
  );
}

function Text({
  name,
  label,
  type = "text",
  step,
  defaultValue,
  required,
  hint,
}: {
  name: string;
  label: string;
  type?: string;
  step?: string;
  defaultValue?: string;
  required?: boolean;
  hint?: string;
}) {
  return (
    <div>
      <label className="label" htmlFor={name}>
        {label}
      </label>
      <input
        id={name}
        name={name}
        type={type}
        step={step}
        required={required}
        defaultValue={defaultValue}
        className="field"
      />
      {hint && <p className="mt-1 text-xs text-ink-500">{hint}</p>}
    </div>
  );
}
