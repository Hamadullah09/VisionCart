import { NextResponse } from "next/server";
import { prisma } from "@/lib/db";
import { getSession } from "@/lib/session";
import { ensurePatientForUser } from "@/lib/auth";
import { storeImage, UploadError } from "@/lib/storage";
import { audit } from "@/lib/audit";

/** Customer-facing upload of a paper prescription photo. */
export async function POST(request: Request) {
  const session = await getSession();
  if (!session) return NextResponse.json({ error: "Please sign in first." }, { status: 401 });

  const form = await request.formData().catch(() => null);
  const file = form?.get("file");
  if (!(file instanceof File)) {
    return NextResponse.json({ error: "No file was included." }, { status: 400 });
  }

  try {
    const stored = await storeImage(file, { folder: "prescriptions" });
    const patient = await ensurePatientForUser(session.userId);

    const doc = await prisma.patientDocument.create({
      data: {
        patientId: patient.id,
        kind: "prescription_scan",
        label: file.name,
        url: stored.url,
        mimeType: stored.mimeType,
        sizeBytes: stored.sizeBytes,
      },
    });

    // Open a draft so it shows in the optician's verification queue rather
    // than sitting as an orphan file nobody is asked to look at.
    await prisma.prescription.create({
      data: {
        patientId: patient.id,
        source: "uploaded",
        status: "pending_verification",
        documentUrl: stored.url,
        notes: "Uploaded by the customer — needs transcribing.",
      },
    });

    await audit({
      userId: session.userId,
      action: "prescription.upload",
      entity: "Patient",
      entityId: patient.id,
      detail: { documentId: doc.id },
    });

    return NextResponse.json({ ok: true, url: stored.url });
  } catch (err) {
    if (err instanceof UploadError) {
      return NextResponse.json({ error: err.message }, { status: 400 });
    }
    console.error("[prescription-upload] failed", err);
    return NextResponse.json({ error: "Upload failed." }, { status: 500 });
  }
}
