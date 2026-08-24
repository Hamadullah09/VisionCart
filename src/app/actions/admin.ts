"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { z } from "zod";
import { prisma } from "@/lib/db";
import { apiStaff, requireStaff, nextFileNo } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { setSetting } from "@/lib/settings";
import { toMinor } from "@/lib/money";
import { markOrderPaid, refundPayment } from "@/lib/payments";
import { createShipmentForOrder } from "@/lib/shipping";
import { deleteStored } from "@/lib/storage";
import { toPrismaRx, prescriptionSchema } from "@/lib/rx";

export type AdminResult = { ok: true; message?: string } | { ok: false; error: string };

/** Every admin action starts here — role check plus the actor for the audit log. */
async function staff() {
  const session = await apiStaff();
  if (!session) throw new Error("Not authorised.");
  return session;
}

function str(fd: FormData, key: string): string {
  return String(fd.get(key) ?? "").trim();
}
function optStr(fd: FormData, key: string): string | null {
  const v = str(fd, key);
  return v.length ? v : null;
}
/** Checkbox groups arrive as repeated keys; store them comma-separated. */
function list(fd: FormData, key: string): string | null {
  const values = fd.getAll(key).map(String).filter(Boolean);
  return values.length ? values.join(",") : null;
}
function bool(fd: FormData, key: string): boolean {
  const v = fd.get(key);
  return v === "on" || v === "true" || v === "1";
}
function int(fd: FormData, key: string, fallback = 0): number {
  const n = Number(str(fd, key));
  return Number.isFinite(n) ? Math.trunc(n) : fallback;
}
function float(fd: FormData, key: string): number | null {
  const raw = str(fd, key);
  if (!raw) return null;
  const n = Number(raw);
  return Number.isFinite(n) ? n : null;
}
/** Prices are typed in major units by staff and stored in minor units. */
function money(fd: FormData, key: string): number {
  const n = Number(str(fd, key));
  return Number.isFinite(n) ? toMinor(n) : 0;
}
function moneyOrNull(fd: FormData, key: string): number | null {
  const raw = str(fd, key);
  if (!raw) return null;
  const n = Number(raw);
  return Number.isFinite(n) ? toMinor(n) : null;
}

function slugify(value: string): string {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 80);
}

/** Frames are sized by their total width; the band drives the size filter. */
function bandFor(totalWidthMm: number | null): string | null {
  if (!totalWidthMm) return null;
  if (totalWidthMm < 130) return "narrow";
  if (totalWidthMm > 143) return "wide";
  return "medium";
}

// ---------------------------------------------------------------------------
// Frames
// ---------------------------------------------------------------------------

export async function saveFrameAction(formData: FormData): Promise<void> {
  const session = await staff();
  const id = optStr(formData, "id");

  const name = str(formData, "name");
  if (!name) throw new Error("A frame needs a name.");

  const totalWidthMm = float(formData, "totalWidthMm");

  const data = {
    name,
    sku: str(formData, "sku") || `F-${Date.now().toString(36).toUpperCase()}`,
    slug: str(formData, "slug") || slugify(name),
    brandId: optStr(formData, "brandId"),
    description: optStr(formData, "description"),
    shape: optStr(formData, "shape"),
    material: optStr(formData, "material"),
    rimType: str(formData, "rimType") || "full_rim",
    gender: str(formData, "gender") || "unisex",
    faceShapes: list(formData, "faceShapes"),
    lensWidthMm: float(formData, "lensWidthMm"),
    bridgeWidthMm: float(formData, "bridgeWidthMm"),
    templeLengthMm: float(formData, "templeLengthMm"),
    lensHeightMm: float(formData, "lensHeightMm"),
    totalWidthMm,
    weightGrams: float(formData, "weightGrams"),
    sizeBand: bandFor(totalWidthMm),
    basePriceMinor: money(formData, "basePrice"),
    compareAtMinor: moneyOrNull(formData, "compareAt"),
    costMinor: moneyOrNull(formData, "cost"),
    allowFrameOnly: bool(formData, "allowFrameOnly"),
    requiresPrescription: bool(formData, "requiresPrescription"),
    status: str(formData, "status") || "draft",
    isFeatured: bool(formData, "isFeatured"),
    position: int(formData, "position"),
    metaTitle: optStr(formData, "metaTitle"),
    metaDesc: optStr(formData, "metaDesc"),
  };

  const frame = id
    ? await prisma.frame.update({ where: { id }, data })
    : await prisma.frame.create({
        data: {
          ...data,
          // A new frame is useless without somewhere to hang stock and images.
          variants: {
            create: {
              sku: `${data.sku}-01`,
              colorName: "Default",
              colorHex: "#333333",
              stockQty: 0,
            },
          },
        },
      });

  await audit({
    userId: session.userId,
    action: id ? "frame.update" : "frame.create",
    entity: "Frame",
    entityId: frame.id,
    detail: { name: frame.name, status: frame.status },
  });

  revalidatePath("/admin/frames");
  revalidatePath(`/frames/${frame.slug}`);
  redirect(`/admin/frames/${frame.id}`);
}

export async function deleteFrameAction(formData: FormData): Promise<void> {
  const session = await staff();
  const id = str(formData, "id");

  // Archive rather than delete: order history references these rows, and a
  // frame that vanishes takes its invoices' meaning with it.
  await prisma.frame.update({ where: { id }, data: { status: "archived" } });
  await audit({ userId: session.userId, action: "frame.archive", entity: "Frame", entityId: id });

  revalidatePath("/admin/frames");
  redirect("/admin/frames");
}

export async function saveVariantAction(
  _prev: AdminResult | null,
  formData: FormData,
): Promise<AdminResult> {
  const session = await staff();
  const id = optStr(formData, "id");
  const frameId = str(formData, "frameId");

  const data = {
    frameId,
    sku: str(formData, "sku"),
    colorName: str(formData, "colorName") || "Default",
    colorHex: optStr(formData, "colorHex"),
    barcode: optStr(formData, "barcode"),
    priceMinor: moneyOrNull(formData, "price"),
    stockQty: int(formData, "stockQty"),
    lowStockAt: int(formData, "lowStockAt", 3),
    isActive: bool(formData, "isActive"),
    position: int(formData, "position"),
    anchorLeftX: float(formData, "anchorLeftX") ?? 0.29,
    anchorLeftY: float(formData, "anchorLeftY") ?? 0.5,
    anchorRightX: float(formData, "anchorRightX") ?? 0.71,
    anchorRightY: float(formData, "anchorRightY") ?? 0.5,
    tryOnScaleAdj: float(formData, "tryOnScaleAdj") ?? 1,
    tryOnOpacity: float(formData, "tryOnOpacity") ?? 1,
  };

  if (!data.sku) return { ok: false, error: "Each colourway needs its own SKU." };

  try {
    const variant = id
      ? await prisma.frameVariant.update({ where: { id }, data })
      : await prisma.frameVariant.create({ data });

    await audit({
      userId: session.userId,
      action: id ? "variant.update" : "variant.create",
      entity: "FrameVariant",
      entityId: variant.id,
      detail: { sku: variant.sku, stockQty: variant.stockQty },
    });

    revalidatePath(`/admin/frames/${frameId}`);
    return { ok: true, message: `Saved ${variant.colorName}.` };
  } catch (err) {
    if (String(err).includes("Unique constraint")) {
      return { ok: false, error: `SKU "${data.sku}" is already used by another colourway.` };
    }
    throw err;
  }
}

export async function deleteVariantAction(formData: FormData): Promise<void> {
  const session = await staff();
  const id = str(formData, "id");
  const frameId = str(formData, "frameId");

  const usedInOrder = await prisma.orderItem.count({ where: { variantId: id } });
  if (usedInOrder > 0) {
    // Keep the row so past orders still resolve; just take it off sale.
    await prisma.frameVariant.update({ where: { id }, data: { isActive: false } });
  } else {
    await prisma.frameVariant.delete({ where: { id } });
  }

  await audit({ userId: session.userId, action: "variant.delete", entity: "FrameVariant", entityId: id });
  revalidatePath(`/admin/frames/${frameId}`);
}

export async function deleteImageAction(formData: FormData): Promise<void> {
  await staff();
  const id = str(formData, "id");
  const frameId = str(formData, "frameId");

  const image = await prisma.productImage.findUnique({ where: { id } });
  if (image) {
    await prisma.productImage.delete({ where: { id } });
    await deleteStored(image.url);
    if (image.thumbUrl) await deleteStored(image.thumbUrl);
  }
  revalidatePath(`/admin/frames/${frameId}`);
}

export async function setPrimaryImageAction(formData: FormData): Promise<void> {
  await staff();
  const id = str(formData, "id");
  const variantId = str(formData, "variantId");
  const frameId = str(formData, "frameId");

  await prisma.$transaction([
    prisma.productImage.updateMany({
      where: { variantId, role: "primary" },
      data: { role: "gallery" },
    }),
    prisma.productImage.update({ where: { id }, data: { role: "primary", position: 0 } }),
  ]);
  revalidatePath(`/admin/frames/${frameId}`);
}

// ---------------------------------------------------------------------------
// Patients & prescriptions
// ---------------------------------------------------------------------------

export async function savePatientAction(formData: FormData): Promise<void> {
  const session = await staff();
  const id = optStr(formData, "id");

  const dob = str(formData, "dateOfBirth");
  const data = {
    firstName: str(formData, "firstName"),
    lastName: str(formData, "lastName"),
    email: optStr(formData, "email"),
    phone: optStr(formData, "phone"),
    dateOfBirth: dob ? new Date(dob) : null,
    gender: optStr(formData, "gender"),
    notes: optStr(formData, "notes"),
    pdMm: float(formData, "pdMm"),
    pdNearMm: float(formData, "pdNearMm"),
    tags: optStr(formData, "tags"),
    consentMarketing: bool(formData, "consentMarketing"),
  };

  if (!data.firstName) throw new Error("A patient file needs at least a first name.");

  const patient = id
    ? await prisma.patient.update({ where: { id }, data })
    : await prisma.patient.create({ data: { ...data, fileNo: await nextFileNo() } });

  await audit({
    userId: session.userId,
    action: id ? "patient.update" : "patient.create",
    entity: "Patient",
    entityId: patient.id,
    // Never write clinical values into the audit detail — the log is read far
    // more widely than the record it describes.
    detail: { fileNo: patient.fileNo, fields: Object.keys(data) },
  });

  revalidatePath("/admin/patients");
  redirect(`/admin/patients/${patient.id}`);
}

export async function savePrescriptionAction(formData: FormData): Promise<void> {
  const session = await staff();
  const id = optStr(formData, "id");
  const patientId = str(formData, "patientId");

  const payload = {
    od: {
      sphere: float(formData, "odSphere"),
      cylinder: float(formData, "odCylinder"),
      axis: float(formData, "odAxis"),
      add: float(formData, "odAdd"),
      prism: float(formData, "odPrism"),
      prismBase: (optStr(formData, "odPrismBase") as "in" | "out" | "up" | "down" | null) ?? null,
      pdMm: float(formData, "odPdMm"),
      segHeightMm: float(formData, "odSegHeightMm"),
    },
    os: {
      sphere: float(formData, "osSphere"),
      cylinder: float(formData, "osCylinder"),
      axis: float(formData, "osAxis"),
      add: float(formData, "osAdd"),
      prism: float(formData, "osPrism"),
      prismBase: (optStr(formData, "osPrismBase") as "in" | "out" | "up" | "down" | null) ?? null,
      pdMm: float(formData, "osPdMm"),
      segHeightMm: float(formData, "osSegHeightMm"),
    },
    pdMm: float(formData, "pdMm"),
    pdNearMm: float(formData, "pdNearMm"),
    prescriber: optStr(formData, "prescriber"),
    clinic: optStr(formData, "clinic"),
    notes: optStr(formData, "notes"),
  };

  const parsed = prescriptionSchema.safeParse(payload);
  if (!parsed.success) {
    throw new Error(parsed.error.issues[0]?.message ?? "Check the prescription values.");
  }

  const issuedAt = str(formData, "issuedAt");
  const expiresAt = str(formData, "expiresAt");
  const status = str(formData, "status") || "draft";

  const data = {
    patientId,
    source: str(formData, "source") || "in_store_exam",
    status,
    issuedAt: issuedAt ? new Date(issuedAt) : new Date(),
    expiresAt: expiresAt ? new Date(expiresAt) : null,
    ...toPrismaRx(parsed.data),
    ...(status === "verified"
      ? { verifiedBy: session.name, verifiedAt: new Date() }
      : { verifiedBy: null, verifiedAt: null }),
  };

  const rx = id
    ? await prisma.prescription.update({ where: { id }, data })
    : await prisma.prescription.create({ data });

  // The binocular PD belongs on the file too — it is reused on every order.
  if (parsed.data.pdMm) {
    await prisma.patient.update({
      where: { id: patientId },
      data: { pdMm: parsed.data.pdMm, pdNearMm: parsed.data.pdNearMm ?? undefined },
    });
  }

  await audit({
    userId: session.userId,
    action: id ? "prescription.update" : "prescription.create",
    entity: "Prescription",
    entityId: rx.id,
    detail: { patientId, status },
  });

  revalidatePath(`/admin/patients/${patientId}`);
  redirect(`/admin/patients/${patientId}`);
}

export async function verifyPrescriptionAction(formData: FormData): Promise<void> {
  const session = await staff();
  const id = str(formData, "id");
  const decision = str(formData, "decision");

  const rx = await prisma.prescription.update({
    where: { id },
    data:
      decision === "reject"
        ? { status: "rejected", verifiedBy: session.name, verifiedAt: new Date() }
        : { status: "verified", verifiedBy: session.name, verifiedAt: new Date() },
  });

  await audit({
    userId: session.userId,
    action: `prescription.${decision === "reject" ? "reject" : "verify"}`,
    entity: "Prescription",
    entityId: id,
  });

  revalidatePath(`/admin/patients/${rx.patientId}`);
  revalidatePath("/admin");
}

// ---------------------------------------------------------------------------
// Orders
// ---------------------------------------------------------------------------

export async function updateOrderAction(formData: FormData): Promise<void> {
  const session = await staff();
  const id = str(formData, "id");

  const status = optStr(formData, "status");
  const fulfilmentStatus = optStr(formData, "fulfilmentStatus");
  const internalNotes = optStr(formData, "internalNotes");

  const before = await prisma.order.findUnique({ where: { id } });
  if (!before) throw new Error("Order not found.");

  await prisma.order.update({
    where: { id },
    data: {
      ...(status ? { status } : {}),
      ...(fulfilmentStatus ? { fulfilmentStatus } : {}),
      ...(internalNotes !== null ? { internalNotes } : {}),
      ...(status === "shipped" && !before.shippedAt ? { shippedAt: new Date() } : {}),
      ...(status === "delivered" && !before.deliveredAt ? { deliveredAt: new Date() } : {}),
      ...(status === "cancelled" && !before.cancelledAt ? { cancelledAt: new Date() } : {}),
    },
  });

  // Cancelling puts the frames back on the shelf.
  if (status === "cancelled" && before.status !== "cancelled") {
    const items = await prisma.orderItem.findMany({ where: { orderId: id } });
    for (const item of items) {
      if (item.variantId) {
        await prisma.frameVariant.update({
          where: { id: item.variantId },
          data: { stockQty: { increment: item.qty } },
        });
      }
    }
  }

  await audit({
    userId: session.userId,
    action: "order.update",
    entity: "Order",
    entityId: id,
    detail: { from: before.status, to: status },
  });

  revalidatePath(`/admin/orders/${id}`);
  revalidatePath("/admin/orders");
}

export async function recordManualPaymentAction(formData: FormData): Promise<void> {
  const session = await staff();
  const orderId = str(formData, "orderId");

  await markOrderPaid({ orderId });
  await audit({
    userId: session.userId,
    action: "payment.manual_confirm",
    entity: "Order",
    entityId: orderId,
    detail: { reference: optStr(formData, "reference") },
  });

  revalidatePath(`/admin/orders/${orderId}`);
}

export async function refundOrderAction(formData: FormData): Promise<void> {
  const session = await staff();
  const orderId = str(formData, "orderId");
  const paymentId = str(formData, "paymentId");
  const amount = moneyOrNull(formData, "amount");

  await refundPayment(paymentId, amount ?? undefined);
  await audit({
    userId: session.userId,
    action: "payment.refund",
    entity: "Order",
    entityId: orderId,
    detail: { paymentId, amountMinor: amount },
  });

  revalidatePath(`/admin/orders/${orderId}`);
}

export async function updateLabStatusAction(formData: FormData): Promise<void> {
  const session = await staff();
  const itemId = str(formData, "itemId");
  const orderId = str(formData, "orderId");
  const labStatus = str(formData, "labStatus");

  await prisma.orderItem.update({
    where: { id: itemId },
    data: { labStatus, labRef: optStr(formData, "labRef") },
  });

  // Move the order along automatically once every line is ready.
  const items = await prisma.orderItem.findMany({ where: { orderId } });
  if (items.every((i) => i.labStatus === "ready")) {
    await prisma.order.update({
      where: { id: orderId },
      data: { status: "ready", fulfilmentStatus: "quality_check" },
    });
  } else if (items.some((i) => i.labStatus !== "pending")) {
    await prisma.order.update({
      where: { id: orderId },
      data: { status: "in_lab", fulfilmentStatus: "lab_processing" },
    });
  }

  await audit({
    userId: session.userId,
    action: "order.lab_status",
    entity: "OrderItem",
    entityId: itemId,
    detail: { labStatus },
  });

  revalidatePath(`/admin/orders/${orderId}`);
}

export async function createShipmentAction(formData: FormData): Promise<void> {
  const session = await staff();
  const orderId = str(formData, "orderId");

  const shipment = await createShipmentForOrder({
    orderId,
    carrier: str(formData, "carrier") || "local",
    service: optStr(formData, "service") ?? undefined,
    costMinor: money(formData, "cost"),
    rateRef: optStr(formData, "rateRef"),
  });

  const tracking = optStr(formData, "trackingNumber");
  if (tracking && !shipment.trackingNumber) {
    await prisma.shipment.update({
      where: { id: shipment.id },
      data: {
        trackingNumber: tracking,
        trackingUrl: optStr(formData, "trackingUrl"),
        status: "in_transit",
        shippedAt: new Date(),
      },
    });
  }

  await prisma.order.update({
    where: { id: orderId },
    data: { status: "shipped", fulfilmentStatus: "shipped", shippedAt: new Date() },
  });

  await audit({
    userId: session.userId,
    action: "order.ship",
    entity: "Order",
    entityId: orderId,
    detail: { shipmentId: shipment.id, tracking },
  });

  revalidatePath(`/admin/orders/${orderId}`);
}

// ---------------------------------------------------------------------------
// Promotions
// ---------------------------------------------------------------------------

const promoSchema = z.object({
  name: z.string().min(2, "Give the deal a name customers will understand."),
  kind: z.string().min(1),
});

export async function savePromotionAction(formData: FormData): Promise<void> {
  const session = await staff();
  const id = optStr(formData, "id");

  const parsed = promoSchema.safeParse({
    name: str(formData, "name"),
    kind: str(formData, "kind"),
  });
  if (!parsed.success) throw new Error(parsed.error.issues[0].message);

  const kind = parsed.data.kind;
  // `value` means different things per kind: basis points for a percentage,
  // minor units for everything money-shaped.
  const rawValue = Number(str(formData, "value")) || 0;
  const value =
    kind === "percent_off" ? Math.round(rawValue * 100) : kind === "free_shipping" ? 0 : toMinor(rawValue);

  const starts = str(formData, "startsAt");
  const ends = str(formData, "endsAt");

  const data = {
    name: parsed.data.name,
    description: optStr(formData, "description"),
    code: optStr(formData, "code")?.toUpperCase() ?? null,
    kind,
    value,
    maxDiscountMinor: moneyOrNull(formData, "maxDiscount"),
    minSubtotalMinor: money(formData, "minSubtotal"),
    minQty: int(formData, "minQty", 1),
    brandIds: list(formData, "brandIds"),
    categoryIds: list(formData, "categoryIds"),
    frameIds: list(formData, "frameIds"),
    firstOrderOnly: bool(formData, "firstOrderOnly"),
    startsAt: starts ? new Date(starts) : null,
    endsAt: ends ? new Date(ends) : null,
    usageLimit: str(formData, "usageLimit") ? int(formData, "usageLimit") : null,
    usageLimitPerUser: str(formData, "usageLimitPerUser") ? int(formData, "usageLimitPerUser") : null,
    stackable: bool(formData, "stackable"),
    priority: int(formData, "priority"),
    isActive: bool(formData, "isActive"),
    bannerText: optStr(formData, "bannerText"),
    bannerColor: optStr(formData, "bannerColor"),
  };

  const promo = id
    ? await prisma.promotion.update({ where: { id }, data })
    : await prisma.promotion.create({ data });

  await audit({
    userId: session.userId,
    action: id ? "promotion.update" : "promotion.create",
    entity: "Promotion",
    entityId: promo.id,
    detail: { name: promo.name, kind, value, code: promo.code },
  });

  revalidatePath("/admin/promotions");
  revalidatePath("/deals");
  revalidatePath("/", "layout");
  redirect("/admin/promotions");
}

export async function togglePromotionAction(formData: FormData): Promise<void> {
  await staff();
  const id = str(formData, "id");
  const promo = await prisma.promotion.findUnique({ where: { id } });
  if (!promo) return;

  await prisma.promotion.update({ where: { id }, data: { isActive: !promo.isActive } });
  revalidatePath("/admin/promotions");
  revalidatePath("/", "layout");
}

export async function deletePromotionAction(formData: FormData): Promise<void> {
  const session = await staff();
  const id = str(formData, "id");

  const used = await prisma.order.count({ where: { promotionId: id } });
  if (used > 0) {
    // Orders reference it; deactivate instead so reporting stays intact.
    await prisma.promotion.update({ where: { id }, data: { isActive: false } });
  } else {
    await prisma.promotion.delete({ where: { id } });
  }

  await audit({ userId: session.userId, action: "promotion.delete", entity: "Promotion", entityId: id });
  revalidatePath("/admin/promotions");
  redirect("/admin/promotions");
}

// ---------------------------------------------------------------------------
// Lens options
// ---------------------------------------------------------------------------

export async function saveLensOptionAction(formData: FormData): Promise<void> {
  const session = await staff();
  const id = optStr(formData, "id");

  const data = {
    group: str(formData, "group"),
    code: str(formData, "code"),
    name: str(formData, "name"),
    description: optStr(formData, "description"),
    priceMinor: money(formData, "price"),
    minSphere: float(formData, "minSphere"),
    maxSphere: float(formData, "maxSphere"),
    maxCylinder: float(formData, "maxCylinder"),
    requires: optStr(formData, "requires"),
    excludes: optStr(formData, "excludes"),
    isDefault: bool(formData, "isDefault"),
    isActive: bool(formData, "isActive"),
    position: int(formData, "position"),
  };

  if (!data.code || !data.name || !data.group) {
    throw new Error("Group, code and name are all required.");
  }

  // One default per group, or the builder pre-selects two contradictory things.
  if (data.isDefault) {
    await prisma.lensOption.updateMany({
      where: { group: data.group, ...(id ? { NOT: { id } } : {}) },
      data: { isDefault: false },
    });
  }

  const option = id
    ? await prisma.lensOption.update({ where: { id }, data })
    : await prisma.lensOption.create({ data });

  await audit({
    userId: session.userId,
    action: id ? "lens.update" : "lens.create",
    entity: "LensOption",
    entityId: option.id,
    detail: { code: option.code, priceMinor: option.priceMinor },
  });

  revalidatePath("/admin/lenses");
  redirect("/admin/lenses");
}

export async function deleteLensOptionAction(formData: FormData): Promise<void> {
  await staff();
  await prisma.lensOption.update({
    where: { id: str(formData, "id") },
    data: { isActive: false },
  });
  revalidatePath("/admin/lenses");
}

// ---------------------------------------------------------------------------
// Settings
// ---------------------------------------------------------------------------

export async function saveSettingsAction(formData: FormData): Promise<void> {
  const session = await requireStaff();
  const entries: string[] = [];

  for (const [key, value] of formData.entries()) {
    if (!key.startsWith("setting.")) continue;
    const settingKey = key.slice("setting.".length);
    // A ticked checkbox posts "on"; store the boolean the readers expect.
    const raw = String(value);
    await setSetting(settingKey, raw === "on" ? "true" : raw, settingKey.split(".")[0]);
    entries.push(settingKey);
  }

  // Unchecked checkboxes are absent from FormData, so booleans need an
  // explicit list of which ones the form rendered.
  for (const key of String(formData.get("__booleans") ?? "").split(",").filter(Boolean)) {
    if (!formData.has(`setting.${key}`)) {
      await setSetting(key, "false", key.split(".")[0]);
      entries.push(key);
    }
  }

  await audit({
    userId: session.userId,
    action: "settings.update",
    entity: "Setting",
    detail: { keys: entries },
  });

  revalidatePath("/admin/settings");
  revalidatePath("/", "layout");
}
