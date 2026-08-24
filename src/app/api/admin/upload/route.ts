import { NextResponse } from "next/server";
import { prisma } from "@/lib/db";
import { apiStaff } from "@/lib/auth";
import { storeImage, UploadError } from "@/lib/storage";
import { audit } from "@/lib/audit";

/**
 * Bulk image intake for the back office.
 *
 * Accepts any number of files in one request and reports per-file results, so
 * a shoot of forty photos with two corrupt files uploads thirty-eight and
 * tells you exactly which two failed instead of rejecting the lot.
 *
 * `variantId` attaches the images to a colourway; without it they land in the
 * media library for attaching later. `role=try_on` marks the transparent PNG
 * used by the virtual mirror and keeps its alpha channel.
 */
export async function POST(request: Request) {
  const session = await apiStaff();
  if (!session) return NextResponse.json({ error: "Not authorised." }, { status: 403 });

  const form = await request.formData().catch(() => null);
  if (!form) return NextResponse.json({ error: "Malformed upload." }, { status: 400 });

  const files = form.getAll("files").filter((f): f is File => f instanceof File);
  if (files.length === 0) {
    return NextResponse.json({ error: "No files were included." }, { status: 400 });
  }

  const variantId = form.get("variantId") ? String(form.get("variantId")) : null;
  const role = String(form.get("role") ?? "gallery");
  const tags = form.get("tags") ? String(form.get("tags")) : null;
  const isTryOn = role === "try_on";

  if (variantId) {
    const exists = await prisma.frameVariant.count({ where: { id: variantId } });
    if (!exists) return NextResponse.json({ error: "That colourway no longer exists." }, { status: 404 });
  }

  const uploaded: { filename: string; url: string; thumbUrl: string }[] = [];
  const failed: { filename: string; error: string }[] = [];

  // A starting position that keeps new images after the existing ones.
  let position = variantId
    ? await prisma.productImage.count({ where: { variantId } })
    : 0;

  for (const file of files) {
    try {
      const stored = await storeImage(file, {
        folder: isTryOn ? "tryon-assets" : "products",
        keepAlpha: isTryOn,
      });

      await prisma.mediaAsset.create({
        data: {
          url: stored.url,
          thumbUrl: stored.thumbUrl,
          filename: stored.filename,
          mimeType: stored.mimeType,
          sizeBytes: stored.sizeBytes,
          width: stored.width,
          height: stored.height,
          tags,
          uploadedBy: session.userId,
        },
      });

      if (variantId) {
        if (isTryOn) {
          // One try-on overlay per colourway — the newest replaces the old.
          await prisma.frameVariant.update({
            where: { id: variantId },
            data: { tryOnImageUrl: stored.url },
          });
        } else {
          await prisma.productImage.create({
            data: {
              variantId,
              url: stored.url,
              thumbUrl: stored.thumbUrl,
              alt: stored.filename.replace(/\.[^.]+$/, ""),
              role: position === 0 ? "primary" : role,
              width: stored.width,
              height: stored.height,
              position: position++,
            },
          });
        }
      }

      uploaded.push({ filename: file.name, url: stored.url, thumbUrl: stored.thumbUrl });
    } catch (err) {
      failed.push({
        filename: file.name,
        error: err instanceof UploadError ? err.message : "Could not process this file.",
      });
      if (!(err instanceof UploadError)) console.error("[upload] failed", file.name, err);
    }
  }

  await audit({
    userId: session.userId,
    action: "media.upload",
    entity: variantId ? "FrameVariant" : "MediaAsset",
    entityId: variantId,
    detail: { uploaded: uploaded.length, failed: failed.length, role },
  });

  return NextResponse.json({ uploaded, failed });
}
