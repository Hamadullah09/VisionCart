import { NextResponse } from "next/server";
import { prisma } from "@/lib/db";
import { getSession } from "@/lib/session";
import { storeImage, UploadError } from "@/lib/storage";
import { ensurePatientForUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { getSettingBool } from "@/lib/settings";

/**
 * Saves a try-on snapshot to the customer's file.
 *
 * This is the only path by which any image from the try-on reaches the server,
 * and it only runs when the customer presses "Save to my file" — the raw
 * photo or camera feed is never sent, just the composited result they chose to
 * keep, together with the PD estimate the optician will double-check.
 */
export async function POST(request: Request) {
  const session = await getSession();
  if (!session) {
    return NextResponse.json(
      { error: "Sign in to save snapshots to your file." },
      { status: 401 },
    );
  }

  // Hiding the button is not enough — a store that has turned snapshot
  // retention off must not accept an image posted directly to this route.
  if (!(await getSettingBool("tryon.storeCustomerPhotos"))) {
    return NextResponse.json(
      { error: "This store doesn't keep try-on photos. Use Download instead." },
      { status: 403 },
    );
  }

  let form: FormData;
  try {
    form = await request.formData();
  } catch {
    return NextResponse.json({ error: "Malformed upload." }, { status: 400 });
  }

  const image = form.get("image");
  const variantId = String(form.get("variantId") ?? "");
  const source = String(form.get("source") ?? "upload") === "camera" ? "camera" : "upload";

  if (!(image instanceof File)) {
    return NextResponse.json({ error: "No image was included." }, { status: 400 });
  }

  const variant = await prisma.frameVariant.findUnique({ where: { id: variantId } });
  if (!variant) {
    return NextResponse.json({ error: "That frame no longer exists." }, { status: 404 });
  }

  const pdMm = numberOrNull(form.get("pdMm"));
  const pdConfidence = numberOrNull(form.get("pdConfidence"));
  const faceShape = form.get("faceShape") ? String(form.get("faceShape")) : null;

  try {
    const stored = await storeImage(image, { folder: "tryon" });
    const patient = await ensurePatientForUser(session.userId);

    const tryOnSession = await prisma.tryOnSession.create({
      data: {
        userId: session.userId,
        patientId: patient.id,
        source,
        faceData: JSON.stringify({ pdMm, pdConfidence, faceShape, at: new Date().toISOString() }),
        snapshots: {
          create: { variantId, imageUrl: stored.url },
        },
      },
      include: { snapshots: true },
    });

    // A confident measurement fills a gap in the file; it never overwrites a PD
    // an optician has already recorded by hand.
    if (pdMm && pdConfidence && pdConfidence >= 0.5 && patient.pdMm == null) {
      await prisma.patient.update({
        where: { id: patient.id },
        data: {
          pdMm,
          faceMetrics: JSON.stringify({ pdMm, pdConfidence, faceShape, source: "tryon" }),
        },
      });
    }

    await audit({
      userId: session.userId,
      action: "tryon.snapshot.save",
      entity: "Patient",
      entityId: patient.id,
      detail: { variantId, pdMm, pdConfidence },
    });

    return NextResponse.json({
      ok: true,
      snapshotId: tryOnSession.snapshots[0]?.id,
      url: stored.url,
    });
  } catch (err) {
    if (err instanceof UploadError) {
      return NextResponse.json({ error: err.message }, { status: 400 });
    }
    console.error("[tryon] snapshot save failed", err);
    return NextResponse.json({ error: "Could not save the snapshot." }, { status: 500 });
  }
}

function numberOrNull(v: FormDataEntryValue | null): number | null {
  if (v == null) return null;
  const n = Number(v);
  return Number.isFinite(n) ? n : null;
}
