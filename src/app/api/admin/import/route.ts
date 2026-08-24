import { NextResponse } from "next/server";
import { prisma } from "@/lib/db";
import { apiStaff, nextFileNo } from "@/lib/auth";
import { toMinor } from "@/lib/money";
import { parseCsvObjects } from "@/lib/csv";
import { audit } from "@/lib/audit";

/**
 * CSV import for catalogue and patient data.
 *
 * Rows are processed independently: a bad row is reported with its line number
 * and the rest still import. Everything is an upsert keyed on a stable
 * business identifier (variant SKU, patient file number), so re-importing a
 * corrected file updates rather than duplicates.
 *
 * `dryRun` validates without writing — the back office runs that first so staff
 * see what a file will do before it does it.
 */
export async function POST(request: Request) {
  const session = await apiStaff();
  if (!session) return NextResponse.json({ error: "Not authorised." }, { status: 403 });

  const form = await request.formData().catch(() => null);
  const file = form?.get("file");
  const type = String(form?.get("type") ?? "frames");
  const dryRun = String(form?.get("dryRun") ?? "") === "true";

  if (!(file instanceof File)) {
    return NextResponse.json({ error: "Choose a CSV file first." }, { status: 400 });
  }
  if (file.size > 5 * 1024 * 1024) {
    return NextResponse.json({ error: "That file is over 5 MB." }, { status: 400 });
  }

  const rows = parseCsvObjects(await file.text());
  if (rows.length === 0) {
    return NextResponse.json(
      { error: "No data rows found. The first line must be the column headers." },
      { status: 400 },
    );
  }

  const job = await prisma.importJob.create({
    data: {
      kind: type,
      filename: file.name,
      status: dryRun ? "completed" : "running",
      totalRows: rows.length,
      createdBy: session.userId,
    },
  });

  const errors: { row: number; message: string }[] = [];
  let ok = 0;

  for (const [i, row] of rows.entries()) {
    const line = i + 2; // +1 for the header, +1 for 1-based line numbers
    try {
      if (type === "frames") await importFrameRow(row, dryRun);
      else if (type === "patients") await importPatientRow(row, dryRun);
      else throw new Error(`Unknown import type "${type}".`);
      ok++;
    } catch (err) {
      errors.push({ row: line, message: err instanceof Error ? err.message : String(err) });
    }
  }

  await prisma.importJob.update({
    where: { id: job.id },
    data: {
      status: errors.length === rows.length ? "failed" : "completed",
      okRows: ok,
      errorRows: errors.length,
      report: JSON.stringify(errors.slice(0, 200)),
      finishedAt: new Date(),
    },
  });

  if (!dryRun) {
    await audit({
      userId: session.userId,
      action: "data.import",
      entity: "ImportJob",
      entityId: job.id,
      detail: { type, ok, failed: errors.length },
    });
  }

  return NextResponse.json({
    jobId: job.id,
    dryRun,
    total: rows.length,
    ok,
    failed: errors.length,
    errors: errors.slice(0, 50),
  });
}

// --- Frames -----------------------------------------------------------------

async function importFrameRow(row: Record<string, string>, dryRun: boolean) {
  const variantSku = row.variant_sku || row.sku;
  const frameSku = row.frame_sku || variantSku;
  const name = row.frame_name || row.name;

  if (!variantSku) throw new Error("variant_sku is required.");
  if (!name) throw new Error("frame_name is required.");

  const price = num(row.price);
  if (price == null) throw new Error("price is required and must be a number.");

  if (dryRun) return;

  let brandId: string | null = null;
  if (row.brand) {
    const brand = await prisma.brand.upsert({
      where: { name: row.brand },
      create: { name: row.brand, slug: slugify(row.brand) },
      update: {},
    });
    brandId = brand.id;
  }

  const totalWidth = num(row.total_width_mm);

  const frame = await prisma.frame.upsert({
    where: { sku: frameSku },
    create: {
      sku: frameSku,
      slug: slugify(`${name}-${frameSku}`),
      name,
      brandId,
      shape: row.shape || null,
      material: row.material || null,
      rimType: row.rim_type || "full_rim",
      gender: row.gender || "unisex",
      lensWidthMm: num(row.lens_width_mm),
      bridgeWidthMm: num(row.bridge_width_mm),
      templeLengthMm: num(row.temple_length_mm),
      totalWidthMm: totalWidth,
      sizeBand: band(totalWidth),
      basePriceMinor: toMinor(price),
      compareAtMinor: num(row.compare_at) != null ? toMinor(num(row.compare_at)!) : null,
      costMinor: num(row.cost) != null ? toMinor(num(row.cost)!) : null,
      description: row.description || null,
      status: row.status || "draft",
    },
    update: {
      name,
      brandId,
      shape: row.shape || undefined,
      material: row.material || undefined,
      rimType: row.rim_type || undefined,
      gender: row.gender || undefined,
      lensWidthMm: num(row.lens_width_mm) ?? undefined,
      bridgeWidthMm: num(row.bridge_width_mm) ?? undefined,
      templeLengthMm: num(row.temple_length_mm) ?? undefined,
      totalWidthMm: totalWidth ?? undefined,
      sizeBand: band(totalWidth) ?? undefined,
      basePriceMinor: toMinor(price),
      compareAtMinor: num(row.compare_at) != null ? toMinor(num(row.compare_at)!) : undefined,
      status: row.status || undefined,
    },
  });

  await prisma.frameVariant.upsert({
    where: { sku: variantSku },
    create: {
      frameId: frame.id,
      sku: variantSku,
      colorName: row.color_name || "Default",
      colorHex: row.color_hex || null,
      barcode: row.barcode || null,
      stockQty: Math.trunc(num(row.stock_qty) ?? 0),
      tryOnImageUrl: row.try_on_image || null,
    },
    update: {
      colorName: row.color_name || undefined,
      colorHex: row.color_hex || undefined,
      barcode: row.barcode || undefined,
      stockQty: num(row.stock_qty) != null ? Math.trunc(num(row.stock_qty)!) : undefined,
      tryOnImageUrl: row.try_on_image || undefined,
    },
  });
}

// --- Patients ---------------------------------------------------------------

async function importPatientRow(row: Record<string, string>, dryRun: boolean) {
  const firstName = row.first_name || row.firstname;
  if (!firstName) throw new Error("first_name is required.");

  const dob = row.date_of_birth ? new Date(row.date_of_birth) : null;
  if (dob && Number.isNaN(dob.getTime())) {
    throw new Error(`date_of_birth "${row.date_of_birth}" is not a valid date (use YYYY-MM-DD).`);
  }

  if (dryRun) return;

  const data = {
    firstName,
    lastName: row.last_name || row.lastname || "",
    email: row.email || null,
    phone: row.phone || null,
    dateOfBirth: dob,
    pdMm: num(row.pd_mm),
    notes: row.notes || null,
    consentMarketing: /^(yes|true|1)$/i.test(row.marketing_consent ?? ""),
  };

  const patient = row.file_no
    ? await prisma.patient.upsert({
        where: { fileNo: row.file_no },
        create: { ...data, fileNo: row.file_no },
        update: data,
      })
    : await prisma.patient.create({ data: { ...data, fileNo: await nextFileNo() } });

  // A prescription in the same row is imported alongside the file, which is
  // how most practice-management exports are shaped.
  const hasRx = row.od_sphere || row.os_sphere || row.od_cylinder || row.os_cylinder;
  if (hasRx) {
    await prisma.prescription.create({
      data: {
        patientId: patient.id,
        source: "imported",
        status: "pending_verification",
        issuedAt: row.issued ? new Date(row.issued) : new Date(),
        expiresAt: row.expires ? new Date(row.expires) : null,
        odSphere: num(row.od_sphere),
        odCylinder: num(row.od_cylinder),
        odAxis: num(row.od_axis) != null ? Math.trunc(num(row.od_axis)!) : null,
        odAdd: num(row.od_add),
        odPdMm: num(row.od_pd),
        osSphere: num(row.os_sphere),
        osCylinder: num(row.os_cylinder),
        osAxis: num(row.os_axis) != null ? Math.trunc(num(row.os_axis)!) : null,
        osAdd: num(row.os_add),
        osPdMm: num(row.os_pd),
        prescriber: row.prescriber || null,
        notes: "Imported — verify before dispensing.",
      },
    });
  }
}

function num(value: string | undefined): number | null {
  if (value == null || value.trim() === "") return null;
  // Tolerate "+1.25", "1,200" and stray currency symbols from spreadsheets.
  const cleaned = value.replace(/[,\s]/g, "").replace(/^\+/, "");
  const n = Number(cleaned);
  if (!Number.isFinite(n)) throw new Error(`"${value}" is not a number.`);
  return n;
}

function band(totalWidthMm: number | null): string | null {
  if (!totalWidthMm) return null;
  if (totalWidthMm < 130) return "narrow";
  if (totalWidthMm > 143) return "wide";
  return "medium";
}

function slugify(value: string): string {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 80);
}
