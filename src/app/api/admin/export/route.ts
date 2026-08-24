import { NextResponse } from "next/server";
import { prisma } from "@/lib/db";
import { apiStaff } from "@/lib/auth";
import { fromMinor } from "@/lib/money";
import { summariseRx } from "@/lib/rx";
import { csvResponse, toCsv } from "@/lib/csv";
import { audit } from "@/lib/audit";

/**
 * CSV exports for the back office.
 *
 * Money is written in major units so the file opens sensibly in a spreadsheet;
 * the matching importer converts back. The frames export is deliberately
 * round-trippable — export, edit prices in Excel, re-import.
 */
export async function GET(request: Request) {
  const session = await apiStaff();
  if (!session) return NextResponse.json({ error: "Not authorised." }, { status: 403 });

  const type = new URL(request.url).searchParams.get("type") ?? "frames";
  const stamp = new Date().toISOString().slice(0, 10);

  await audit({ userId: session.userId, action: "data.export", entity: "Export", detail: { type } });

  switch (type) {
    case "frames": {
      const variants = await prisma.frameVariant.findMany({
        include: { frame: { include: { brand: true } } },
        orderBy: [{ frame: { name: "asc" } }, { position: "asc" }],
      });

      const rows = variants.map((v) => ({
        frame_sku: v.frame.sku,
        frame_name: v.frame.name,
        brand: v.frame.brand?.name ?? "",
        variant_sku: v.sku,
        color_name: v.colorName,
        color_hex: v.colorHex ?? "",
        price: fromMinor(v.priceMinor ?? v.frame.basePriceMinor),
        compare_at: v.frame.compareAtMinor ? fromMinor(v.frame.compareAtMinor) : "",
        cost: v.frame.costMinor ? fromMinor(v.frame.costMinor) : "",
        stock_qty: v.stockQty,
        shape: v.frame.shape ?? "",
        material: v.frame.material ?? "",
        rim_type: v.frame.rimType,
        gender: v.frame.gender,
        lens_width_mm: v.frame.lensWidthMm ?? "",
        bridge_width_mm: v.frame.bridgeWidthMm ?? "",
        temple_length_mm: v.frame.templeLengthMm ?? "",
        total_width_mm: v.frame.totalWidthMm ?? "",
        status: v.frame.status,
        try_on_image: v.tryOnImageUrl ?? "",
        barcode: v.barcode ?? "",
      }));

      return csvResponse(`frames-${stamp}.csv`, toCsv(rows));
    }

    case "patients": {
      const patients = await prisma.patient.findMany({
        where: { deletedAt: null },
        include: {
          prescriptions: { orderBy: { issuedAt: "desc" }, take: 1 },
          _count: { select: { orders: true } },
        },
        orderBy: { fileNo: "asc" },
      });

      const rows = patients.map((p) => ({
        file_no: p.fileNo,
        first_name: p.firstName,
        last_name: p.lastName,
        email: p.email ?? "",
        phone: p.phone ?? "",
        date_of_birth: p.dateOfBirth ? p.dateOfBirth.toISOString().slice(0, 10) : "",
        pd_mm: p.pdMm ?? "",
        latest_prescription: p.prescriptions[0] ? summariseRx(p.prescriptions[0]) : "",
        latest_rx_status: p.prescriptions[0]?.status ?? "",
        latest_rx_date: p.prescriptions[0]?.issuedAt.toISOString().slice(0, 10) ?? "",
        orders: p._count.orders,
        marketing_consent: p.consentMarketing ? "yes" : "no",
        created: p.createdAt.toISOString().slice(0, 10),
      }));

      return csvResponse(`patients-${stamp}.csv`, toCsv(rows));
    }

    case "orders": {
      const orders = await prisma.order.findMany({
        include: {
          items: true,
          patient: { select: { fileNo: true } },
          shippingAddress: true,
        },
        orderBy: { placedAt: "desc" },
      });

      // One row per line so the file is usable for lab planning, not just
      // revenue totals.
      const rows = orders.flatMap((o) =>
        o.items.map((i) => ({
          order_no: o.orderNo,
          placed: o.placedAt.toISOString(),
          status: o.status,
          payment_status: o.paymentStatus,
          patient_file: o.patient?.fileNo ?? "",
          email: o.email,
          phone: o.phone ?? "",
          item: i.titleSnapshot,
          item_sku: i.skuSnapshot,
          lenses: i.lensSummary ?? "",
          lab_status: i.labStatus,
          qty: i.qty,
          frame_price: fromMinor(i.unitPriceMinor),
          lens_price: fromMinor(i.lensPriceMinor),
          line_total: fromMinor(i.totalMinor),
          order_total: fromMinor(o.totalMinor),
          currency: o.currency,
          promo: o.promoCode ?? "",
          city: o.shippingAddress?.city ?? "",
          country: o.shippingAddress?.country ?? "",
        })),
      );

      return csvResponse(`orders-${stamp}.csv`, toCsv(rows));
    }

    case "prescriptions": {
      const rx = await prisma.prescription.findMany({
        include: { patient: { select: { fileNo: true, firstName: true, lastName: true } } },
        orderBy: { issuedAt: "desc" },
      });

      const rows = rx.map((r) => ({
        file_no: r.patient.fileNo,
        patient: `${r.patient.firstName} ${r.patient.lastName}`.trim(),
        issued: r.issuedAt.toISOString().slice(0, 10),
        expires: r.expiresAt?.toISOString().slice(0, 10) ?? "",
        status: r.status,
        od_sphere: r.odSphere ?? "",
        od_cylinder: r.odCylinder ?? "",
        od_axis: r.odAxis ?? "",
        od_add: r.odAdd ?? "",
        od_pd: r.odPdMm ?? "",
        os_sphere: r.osSphere ?? "",
        os_cylinder: r.osCylinder ?? "",
        os_axis: r.osAxis ?? "",
        os_add: r.osAdd ?? "",
        os_pd: r.osPdMm ?? "",
        prescriber: r.prescriber ?? "",
        verified_by: r.verifiedBy ?? "",
      }));

      return csvResponse(`prescriptions-${stamp}.csv`, toCsv(rows));
    }

    default:
      return NextResponse.json({ error: `Unknown export type "${type}".` }, { status: 400 });
  }
}
