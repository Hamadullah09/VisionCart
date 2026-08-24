"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";

type Result = {
  dryRun: boolean;
  total: number;
  ok: number;
  failed: number;
  errors: { row: number; message: string }[];
};

/**
 * CSV import with a compulsory dry run first: staff see exactly what a file
 * will do — and which rows are broken — before anything is written.
 */
export default function CsvImporter() {
  const router = useRouter();
  const [type, setType] = useState<"frames" | "patients">("frames");
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<Result | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run(dryRun: boolean) {
    if (!file) {
      setError("Choose a CSV file first.");
      return;
    }
    setBusy(true);
    setError(null);

    const body = new FormData();
    body.append("file", file);
    body.append("type", type);
    body.append("dryRun", String(dryRun));

    try {
      const res = await fetch("/api/admin/import", { method: "POST", body });
      const json = await res.json();
      if (!res.ok) throw new Error(json.error || "Import failed.");
      setResult(json);
      if (!dryRun) router.refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Import failed.");
    } finally {
      setBusy(false);
    }
  }

  const checked = result?.dryRun === true && result.failed < result.total;

  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <div>
          <label className="label" htmlFor="import-type">
            What are you importing?
          </label>
          <select
            id="import-type"
            value={type}
            onChange={(e) => {
              setType(e.target.value as "frames" | "patients");
              setResult(null);
            }}
            className="field"
          >
            <option value="frames">Frames &amp; stock</option>
            <option value="patients">Patients &amp; prescriptions</option>
          </select>
        </div>

        <div>
          <label className="label" htmlFor="import-file">
            CSV file
          </label>
          <input
            id="import-file"
            type="file"
            accept=".csv,text/csv"
            onChange={(e) => {
              setFile(e.target.files?.[0] ?? null);
              setResult(null);
            }}
            className="field py-1.5"
          />
        </div>
      </div>

      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          onClick={() => void run(true)}
          disabled={busy || !file}
          className="btn-secondary"
        >
          {busy ? "Checking…" : "Check the file"}
        </button>
        <button
          type="button"
          onClick={() => void run(false)}
          disabled={busy || !checked}
          className="btn-primary"
          title={checked ? undefined : "Run the check first"}
        >
          Import for real
        </button>
      </div>

      {error && <p className="rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</p>}

      {result && (
        <div
          className={`rounded-lg px-4 py-3 text-sm ${
            result.failed > 0 ? "bg-amber-50 text-amber-900" : "bg-emerald-50 text-emerald-900"
          }`}
        >
          <p className="font-medium">
            {result.dryRun ? "Check complete" : "Import complete"} — {result.ok} of {result.total}{" "}
            rows {result.dryRun ? "would import" : "imported"}
            {result.failed > 0 && `, ${result.failed} had problems`}.
          </p>

          {result.errors.length > 0 && (
            <ul className="mt-2 max-h-52 space-y-0.5 overflow-y-auto font-mono text-xs">
              {result.errors.map((e) => (
                <li key={e.row}>
                  Line {e.row}: {e.message}
                </li>
              ))}
            </ul>
          )}

          {result.dryRun && result.ok > 0 && (
            <p className="mt-2">Happy with that? Press &ldquo;Import for real&rdquo;.</p>
          )}
        </div>
      )}
    </div>
  );
}
