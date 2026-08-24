import { savePatientAction } from "@/app/actions/admin";
import type { Patient } from "@prisma/client";

export default function PatientForm({ patient }: { patient?: Patient | null }) {
  const dob = patient?.dateOfBirth?.toISOString().slice(0, 10) ?? "";

  return (
    <form action={savePatientAction} className="card space-y-4 p-5">
      {patient && <input type="hidden" name="id" value={patient.id} />}

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <Text name="firstName" label="First name" defaultValue={patient?.firstName} required />
        <Text name="lastName" label="Last name" defaultValue={patient?.lastName} />
        <Text name="phone" label="Phone" defaultValue={patient?.phone ?? ""} />
        <Text name="email" label="Email" type="email" defaultValue={patient?.email ?? ""} />
        <Text name="dateOfBirth" label="Date of birth" type="date" defaultValue={dob} />
        <div>
          <label className="label" htmlFor="gender">
            Gender
          </label>
          <select id="gender" name="gender" defaultValue={patient?.gender ?? ""} className="field">
            <option value="">Prefer not to say</option>
            <option value="female">Female</option>
            <option value="male">Male</option>
            <option value="other">Other</option>
          </select>
        </div>
        <Text
          name="pdMm"
          label="Distance PD (mm)"
          type="number"
          step="0.5"
          defaultValue={patient?.pdMm?.toString() ?? ""}
        />
        <Text
          name="pdNearMm"
          label="Near PD (mm)"
          type="number"
          step="0.5"
          defaultValue={patient?.pdNearMm?.toString() ?? ""}
        />
        <Text
          name="tags"
          label="Tags"
          defaultValue={patient?.tags ?? ""}
          hint="Comma separated, e.g. varifocal, dry-eye"
        />
      </div>

      <div>
        <label className="label" htmlFor="notes">
          Clinical notes
        </label>
        <textarea
          id="notes"
          name="notes"
          rows={3}
          defaultValue={patient?.notes ?? ""}
          className="field"
          placeholder="Anything the dispenser should know before making the next pair."
        />
      </div>

      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          name="consentMarketing"
          defaultChecked={patient?.consentMarketing ?? false}
        />
        Consented to marketing contact
      </label>

      <button type="submit" className="btn-primary">
        {patient ? "Save file" : "Create patient file"}
      </button>
    </form>
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
