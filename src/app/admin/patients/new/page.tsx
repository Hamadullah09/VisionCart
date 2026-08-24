import Link from "next/link";
import PatientForm from "@/components/admin/PatientForm";

export const metadata = { title: "New patient file" };

export default function NewPatientPage() {
  return (
    <div className="max-w-4xl space-y-6">
      <div>
        <Link href="/admin/patients" className="text-sm text-brand-600">
          ← Patients
        </Link>
        <h1 className="mt-1 text-2xl font-semibold">New patient file</h1>
        <p className="text-sm text-ink-600">
          Create the file first, then add the prescription from the file page.
        </p>
      </div>

      <PatientForm />
    </div>
  );
}
