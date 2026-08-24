import { z } from "zod";
import { formatDiopter } from "./constants";

/**
 * Prescription handling. Kept separate from pricing because the same shape is
 * used by the customer wizard, the optician's back-office form and CSV import.
 */

const quarterStep = (v: number) => Math.abs(v * 4 - Math.round(v * 4)) < 1e-6;

const sphere = z
  .number()
  .min(-20, "Sphere below -20.00 needs a custom lab order — please call us.")
  .max(20, "Sphere above +20.00 needs a custom lab order — please call us.")
  .refine(quarterStep, "Sphere must be in steps of 0.25.");

const cylinder = z
  .number()
  .min(-6, "Cylinder below -6.00 needs a custom lab order — please call us.")
  .max(6, "Cylinder above +6.00 needs a custom lab order — please call us.")
  .refine(quarterStep, "Cylinder must be in steps of 0.25.");

const axis = z.number().int().min(0).max(180);
const add = z.number().min(0.75).max(3.5).refine(quarterStep, "Add must be in steps of 0.25.");

export const eyeSchema = z.object({
  sphere: sphere.nullable().optional(),
  cylinder: cylinder.nullable().optional(),
  axis: axis.nullable().optional(),
  add: add.nullable().optional(),
  prism: z.number().min(0).max(10).nullable().optional(),
  prismBase: z.enum(["in", "out", "up", "down"]).nullable().optional(),
  pdMm: z.number().min(20).max(40).nullable().optional(),
  segHeightMm: z.number().min(10).max(40).nullable().optional(),
});

export const prescriptionSchema = z
  .object({
    od: eyeSchema,
    os: eyeSchema,
    pdMm: z.number().min(40).max(80).nullable().optional(),
    pdNearMm: z.number().min(40).max(80).nullable().optional(),
    issuedAt: z.string().optional(),
    expiresAt: z.string().nullable().optional(),
    prescriber: z.string().max(120).nullable().optional(),
    clinic: z.string().max(120).nullable().optional(),
    notes: z.string().max(2000).nullable().optional(),
  })
  .superRefine((rx, ctx) => {
    // A cylinder value is meaningless without the axis it sits on, and vice
    // versa. Catching it here saves a remake at the lab.
    for (const side of ["od", "os"] as const) {
      const eye = rx[side];
      const hasCyl = eye.cylinder !== null && eye.cylinder !== undefined && eye.cylinder !== 0;
      const hasAxis = eye.axis !== null && eye.axis !== undefined;
      if (hasCyl && !hasAxis) {
        ctx.addIssue({
          code: "custom",
          path: [side, "axis"],
          message: "Axis is required when a cylinder value is given.",
        });
      }
      if (hasAxis && !hasCyl) {
        ctx.addIssue({
          code: "custom",
          path: [side, "cylinder"],
          message: "Cylinder is required when an axis is given.",
        });
      }
      if (eye.prism && !eye.prismBase) {
        ctx.addIssue({
          code: "custom",
          path: [side, "prismBase"],
          message: "Prism base direction is required.",
        });
      }
    }

    const bothEmpty =
      rx.od.sphere == null && rx.os.sphere == null && rx.od.cylinder == null && rx.os.cylinder == null;
    if (bothEmpty) {
      ctx.addIssue({
        code: "custom",
        path: ["od", "sphere"],
        message: "Enter at least one eye's prescription, or choose non-prescription lenses.",
      });
    }

    const monoPd = rx.od.pdMm != null || rx.os.pdMm != null;
    if (!monoPd && rx.pdMm == null) {
      ctx.addIssue({
        code: "custom",
        path: ["pdMm"],
        message: "Pupillary distance is required — measure it with the try-on tool if you don't know it.",
      });
    }
  });

export type PrescriptionInput = z.infer<typeof prescriptionSchema>;

/** Flatten the nested wizard shape into the flat columns Prisma stores. */
export function toPrismaRx(rx: PrescriptionInput) {
  return {
    odSphere: rx.od.sphere ?? null,
    odCylinder: rx.od.cylinder ?? null,
    odAxis: rx.od.axis ?? null,
    odAdd: rx.od.add ?? null,
    odPrism: rx.od.prism ?? null,
    odPrismBase: rx.od.prismBase ?? null,
    odPdMm: rx.od.pdMm ?? null,
    odSegHeightMm: rx.od.segHeightMm ?? null,
    osSphere: rx.os.sphere ?? null,
    osCylinder: rx.os.cylinder ?? null,
    osAxis: rx.os.axis ?? null,
    osAdd: rx.os.add ?? null,
    osPrism: rx.os.prism ?? null,
    osPrismBase: rx.os.prismBase ?? null,
    osPdMm: rx.os.pdMm ?? null,
    osSegHeightMm: rx.os.segHeightMm ?? null,
    prescriber: rx.prescriber ?? null,
    clinic: rx.clinic ?? null,
    notes: rx.notes ?? null,
  };
}

export type FlatRx = {
  odSphere?: number | null;
  odCylinder?: number | null;
  odAxis?: number | null;
  odAdd?: number | null;
  osSphere?: number | null;
  osCylinder?: number | null;
  osAxis?: number | null;
  osAdd?: number | null;
};

/**
 * Strongest absolute sphere across both eyes — this is what decides whether a
 * thin-index lens is required and whether a frame is a sensible choice.
 */
export function strongestSphere(rx: FlatRx): number {
  return Math.max(Math.abs(rx.odSphere ?? 0), Math.abs(rx.osSphere ?? 0));
}

export function strongestCylinder(rx: FlatRx): number {
  return Math.max(Math.abs(rx.odCylinder ?? 0), Math.abs(rx.osCylinder ?? 0));
}

/** True when either eye carries a reading addition (progressive/bifocal). */
export function needsAddition(rx: FlatRx): boolean {
  return Boolean(rx.odAdd || rx.osAdd);
}

/**
 * The thinnest lens the customer can get away with. Used to pre-select the
 * index step and to warn when the chosen one will look bottle-thick.
 */
export function recommendedIndex(rx: FlatRx): "1.50" | "1.61" | "1.67" | "1.74" {
  const s = strongestSphere(rx);
  if (s >= 8) return "1.74";
  if (s >= 5.5) return "1.67";
  if (s >= 3) return "1.61";
  return "1.50";
}

/** One-line summary for order lines, invoices and lab tickets. */
export function summariseRx(rx: FlatRx): string {
  const eye = (s?: number | null, c?: number | null, a?: number | null, ad?: number | null) => {
    if (s == null && c == null) return "—";
    const parts = [formatDiopter(s ?? 0)];
    if (c) parts.push(`${formatDiopter(c)} x ${a ?? "?"}°`);
    if (ad) parts.push(`Add ${formatDiopter(ad)}`);
    return parts.join(" ");
  };
  return `OD ${eye(rx.odSphere, rx.odCylinder, rx.odAxis, rx.odAdd)} | OS ${eye(
    rx.osSphere,
    rx.osCylinder,
    rx.osAxis,
    rx.osAdd,
  )}`;
}
