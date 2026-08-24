"use client";

import { useRef, useState } from "react";
import { useRouter } from "next/navigation";

/**
 * Drag-and-drop image intake.
 *
 * Files upload one at a time rather than in a single giant request: progress
 * is honest, a 40-photo shoot doesn't hit a body-size limit, and one bad file
 * cannot take the batch down with it.
 */

type Row = {
  name: string;
  status: "queued" | "uploading" | "done" | "error";
  message?: string;
  thumbUrl?: string;
};

export default function BulkUploader({
  variantId,
  role = "gallery",
  label = "Drop images here",
  hint = "JPG, PNG or WebP, up to 15 MB each.",
  onDone,
}: {
  variantId?: string;
  role?: "gallery" | "try_on" | "lifestyle" | "swatch";
  label?: string;
  hint?: string;
  onDone?: () => void;
}) {
  const router = useRouter();
  const inputRef = useRef<HTMLInputElement>(null);
  const [rows, setRows] = useState<Row[]>([]);
  const [dragging, setDragging] = useState(false);
  const [busy, setBusy] = useState(false);
  const [tags, setTags] = useState("");

  async function upload(files: File[]) {
    if (files.length === 0) return;
    setBusy(true);
    setRows(files.map((f) => ({ name: f.name, status: "queued" })));

    for (const [i, file] of files.entries()) {
      setRows((r) => r.map((row, j) => (i === j ? { ...row, status: "uploading" } : row)));

      const body = new FormData();
      body.append("files", file);
      if (variantId) body.append("variantId", variantId);
      body.append("role", role);
      if (tags.trim()) body.append("tags", tags.trim());

      try {
        const res = await fetch("/api/admin/upload", { method: "POST", body });
        const json = await res.json();

        if (!res.ok) throw new Error(json.error || "Upload failed");
        const failure = json.failed?.[0];
        if (failure) throw new Error(failure.error);

        const ok = json.uploaded?.[0];
        setRows((r) =>
          r.map((row, j) =>
            i === j ? { ...row, status: "done", thumbUrl: ok?.thumbUrl } : row,
          ),
        );
      } catch (err) {
        setRows((r) =>
          r.map((row, j) =>
            i === j
              ? { ...row, status: "error", message: err instanceof Error ? err.message : "Failed" }
              : row,
          ),
        );
      }
    }

    setBusy(false);
    router.refresh();
    onDone?.();
  }

  const done = rows.filter((r) => r.status === "done").length;
  const failed = rows.filter((r) => r.status === "error").length;

  return (
    <div>
      <div
        onDragOver={(e) => {
          e.preventDefault();
          setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={(e) => {
          e.preventDefault();
          setDragging(false);
          void upload(Array.from(e.dataTransfer.files).filter((f) => f.type.startsWith("image/")));
        }}
        onClick={() => inputRef.current?.click()}
        role="button"
        tabIndex={0}
        onKeyDown={(e) => {
          if (e.key === "Enter" || e.key === " ") inputRef.current?.click();
        }}
        className={`cursor-pointer rounded-xl border-2 border-dashed p-8 text-center transition ${
          dragging ? "border-brand-500 bg-brand-50" : "border-ink-300 bg-white hover:border-ink-400"
        }`}
      >
        <p className="font-medium">{label}</p>
        <p className="mt-1 text-sm text-ink-500">{hint}</p>
        <p className="mt-2 text-xs text-ink-400">or click to choose files</p>

        <input
          ref={inputRef}
          type="file"
          accept="image/*"
          multiple
          className="hidden"
          onChange={(e) => {
            void upload(Array.from(e.target.files ?? []));
            e.target.value = "";
          }}
        />
      </div>

      {!variantId && (
        <div className="mt-3">
          <label className="label" htmlFor="upload-tags">
            Tag this batch (optional)
          </label>
          <input
            id="upload-tags"
            value={tags}
            onChange={(e) => setTags(e.target.value)}
            placeholder="e.g. autumn-shoot, acetate"
            className="field"
          />
        </div>
      )}

      {rows.length > 0 && (
        <div className="mt-4">
          <p className="text-sm font-medium">
            {busy
              ? `Uploading ${done + failed + 1} of ${rows.length}…`
              : `${done} uploaded${failed ? `, ${failed} failed` : ""}`}
          </p>

          <ul className="mt-2 max-h-60 space-y-1 overflow-y-auto">
            {rows.map((r, i) => (
              <li
                key={`${r.name}-${i}`}
                className="flex items-center gap-3 rounded-lg border border-ink-200 bg-white px-3 py-2 text-sm"
              >
                {r.thumbUrl ? (
                  // eslint-disable-next-line @next/next/no-img-element
                  <img src={r.thumbUrl} alt="" className="h-8 w-10 rounded object-contain" />
                ) : (
                  <span className="h-8 w-10 rounded bg-ink-100" />
                )}
                <span className="min-w-0 flex-1 truncate">{r.name}</span>
                <span
                  className={
                    r.status === "done"
                      ? "text-emerald-700"
                      : r.status === "error"
                        ? "text-rose-700"
                        : "text-ink-500"
                  }
                >
                  {r.status === "done"
                    ? "✓"
                    : r.status === "error"
                      ? (r.message ?? "Failed")
                      : r.status === "uploading"
                        ? "…"
                        : "waiting"}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
