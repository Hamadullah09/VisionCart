import { savePrescriptionAction } from "@/app/actions/admin";
import {
  ADD_VALUES,
  AXIS_VALUES,
  CYLINDER_VALUES,
  PRISM_BASES,
  RX_SOURCES,
  RX_STATUSES,
  SPHERE_VALUES,
  formatDiopter,
  humanise,
} from "@/lib/constants";
import type { Prescription } from "@prisma/client";

/**
 * The optician's prescription form. Every diopter field is a select stepping
 * in 0.25 D — a free-text box here is how a lab ends up cutting -2.13.
 */
export default function PrescriptionForm({
  patientId,
  prescription,
  defaultPdMm,
  defaultPdNearMm,
}: {
  patientId: string;
  prescription?: Prescription | null;
  /** Falls back to the PD already recorded on the patient file. */
  defaultPdMm?: number | null;
  defaultPdNearMm?: number | null;
}) {
  const rx = prescription;
  const date = (d: Date | null | undefined) => d?.toISOString().slice(0, 10) ?? "";

  return (
    <form action={savePrescriptionAction} className="space-y-4">
      <input type="hidden" name="patientId" value={patientId} />
      {rx && <input type="hidden" name="id" value={rx.id} />}

      <div className="table-wrap">
        <table className="table min-w-[52rem]">
          <thead>
            <tr>
              <th>Eye</th>
              <th>SPH</th>
              <th>CYL</th>
              <th>Axis</th>
              <th>Add</th>
              <th>Prism</th>
              <th>Base</th>
              <th>Mono PD</th>
              <th>Seg ht</th>
            </tr>
          </thead>
          <tbody>
            <EyeRow eye="od" label="Right (OD)" rx={rx} />
            <EyeRow eye="os" label="Left (OS)" rx={rx} />
          </tbody>
        </table>
      </div>

      <div className="grid gap-4 sm:grid-cols-3 lg:grid-cols-4">
        <Num
          name="pdMm"
          label="Distance PD (mm)"
          step="0.5"
          defaultValue={defaultPdMm?.toString() ?? ""}
        />
        <Num
          name="pdNearMm"
          label="Near PD (mm)"
          step="0.5"
          defaultValue={defaultPdNearMm?.toString() ?? ""}
        />
        <Text name="prescriber" label="Prescriber" defaultValue={rx?.prescriber ?? ""} />
        <Text name="clinic" label="Clinic" defaultValue={rx?.clinic ?? ""} />
        <Text name="issuedAt" label="Issued" type="date" defaultValue={date(rx?.issuedAt)} />
        <Text name="expiresAt" label="Expires" type="date" defaultValue={date(rx?.expiresAt)} />
        <div>
          <label className="label" htmlFor="source">
            Source
          </label>
          <select id="source" name="source" defaultValue={rx?.source ?? "in_store_exam"} className="field">
            {RX_SOURCES.map((s) => (
              <option key={s} value={s}>
                {humanise(s)}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label className="label" htmlFor="status">
            Status
          </label>
          <select id="status" name="status" defaultValue={rx?.status ?? "draft"} className="field">
            {RX_STATUSES.map((s) => (
              <option key={s} value={s}>
                {humanise(s)}
              </option>
            ))}
          </select>
        </div>
      </div>

      <div>
        <label className="label" htmlFor="rx-notes">
          Notes
        </label>
        <textarea
          id="rx-notes"
          name="notes"
          rows={2}
          defaultValue={rx?.notes ?? ""}
          className="field"
        />
      </div>

      <button type="submit" className="btn-primary">
        {rx ? "Save prescription" : "Add prescription"}
      </button>
    </form>
  );
}

function EyeRow({
  eye,
  label,
  rx,
}: {
  eye: "od" | "os";
  label: string;
  rx?: Prescription | null;
}) {
  const v = (suffix: string) =>
    (rx?.[`${eye}${suffix}` as keyof Prescription] as number | null | undefined)?.toString() ?? "";

  return (
    <tr>
      <td className="font-medium whitespace-nowrap">{label}</td>
      <td>
        <DiopterSelect name={`${eye}Sphere`} values={SPHERE_VALUES} defaultValue={v("Sphere")} />
      </td>
      <td>
        <DiopterSelect name={`${eye}Cylinder`} values={CYLINDER_VALUES} defaultValue={v("Cylinder")} />
      </td>
      <td>
        <select name={`${eye}Axis`} defaultValue={v("Axis")} className="field py-1.5">
          <option value="">—</option>
          {AXIS_VALUES.map((a) => (
            <option key={a} value={a}>
              {a}
            </option>
          ))}
        </select>
      </td>
      <td>
        <DiopterSelect name={`${eye}Add`} values={ADD_VALUES} defaultValue={v("Add")} />
      </td>
      <td>
        <input
          name={`${eye}Prism`}
          type="number"
          step="0.25"
          min="0"
          max="10"
          defaultValue={v("Prism")}
          className="field w-20 py-1.5"
        />
      </td>
      <td>
        <select name={`${eye}PrismBase`} defaultValue={rx?.[`${eye}PrismBase` as keyof Prescription] as string ?? ""} className="field py-1.5">
          <option value="">—</option>
          {PRISM_BASES.map((b) => (
            <option key={b} value={b}>
              {humanise(b)}
            </option>
          ))}
        </select>
      </td>
      <td>
        <input
          name={`${eye}PdMm`}
          type="number"
          step="0.5"
          defaultValue={v("PdMm")}
          className="field w-20 py-1.5"
        />
      </td>
      <td>
        <input
          name={`${eye}SegHeightMm`}
          type="number"
          step="0.5"
          defaultValue={v("SegHeightMm")}
          className="field w-20 py-1.5"
        />
      </td>
    </tr>
  );
}

function DiopterSelect({
  name,
  values,
  defaultValue,
}: {
  name: string;
  values: number[];
  defaultValue?: string;
}) {
  return (
    <select name={name} defaultValue={defaultValue} className="field py-1.5">
      <option value="">—</option>
      {values.map((v) => (
        <option key={v} value={v}>
          {formatDiopter(v)}
        </option>
      ))}
    </select>
  );
}

function Text({
  name,
  label,
  type = "text",
  defaultValue,
}: {
  name: string;
  label: string;
  type?: string;
  defaultValue?: string;
}) {
  return (
    <div>
      <label className="label" htmlFor={name}>
        {label}
      </label>
      <input id={name} name={name} type={type} defaultValue={defaultValue} className="field" />
    </div>
  );
}

function Num({
  name,
  label,
  step,
  defaultValue,
}: {
  name: string;
  label: string;
  step?: string;
  defaultValue?: string;
}) {
  return (
    <div>
      <label className="label" htmlFor={name}>
        {label}
      </label>
      <input
        id={name}
        name={name}
        type="number"
        step={step}
        defaultValue={defaultValue}
        className="field"
      />
    </div>
  );
}
