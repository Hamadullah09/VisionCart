"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";

/**
 * Sends a photo of a paper prescription to the patient's file. It is filed as
 * a document for an optician to transcribe rather than parsed automatically —
 * a misread cylinder axis is a remake and a headache, so a human checks it.
 */
export default function RxUploader() {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function upload(file: File) {
    setBusy(true);
    setError(null);
    setMessage(null);
    try {
      const body = new FormData();
      body.append("file", file);
      body.append("kind", "prescription_scan");

      const res = await fetch("/api/account/prescription-upload", { method: "POST", body });
      const json = await res.json();
      if (!res.ok) throw new Error(json.error || "Upload failed.");

      setMessage("Received — our optician will add it to your file shortly.");
      router.refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Upload failed.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <label className="btn-secondary cursor-pointer">
        {busy ? "Uploading…" : "Choose a photo or scan"}
        <input
          type="file"
          accept="image/*"
          className="hidden"
          disabled={busy}
          onChange={(e) => {
            const f = e.target.files?.[0];
            if (f) void upload(f);
            e.target.value = "";
          }}
        />
      </label>

      {message && <p className="mt-2 text-sm text-emerald-700">{message}</p>}
      {error && <p className="mt-2 text-sm text-rose-700">{error}</p>}
    </div>
  );
}
