/**
 * The schema stores these as plain strings so it stays portable across SQLite,
 * Postgres and MySQL. This file is the single source of truth for what those
 * strings may be — validation, dropdowns and labels all read from here.
 */

export const ROLES = ["customer", "staff", "optician", "admin"] as const;
export type Role = (typeof ROLES)[number];

/** Roles allowed into /admin, weakest first. */
export const STAFF_ROLES: Role[] = ["staff", "optician", "admin"];

export const ROLE_LABELS: Record<Role, string> = {
  customer: "Customer",
  staff: "Staff",
  optician: "Optician",
  admin: "Administrator",
};

export const FRAME_SHAPES = [
  "rectangle",
  "square",
  "round",
  "oval",
  "aviator",
  "cat_eye",
  "wayfarer",
  "geometric",
  "browline",
] as const;

export const FRAME_MATERIALS = [
  "acetate",
  "metal",
  "titanium",
  "tr90",
  "stainless",
  "mixed",
  "wood",
] as const;

export const RIM_TYPES = ["full_rim", "semi_rimless", "rimless"] as const;
export const GENDERS = ["men", "women", "unisex", "kids"] as const;
export const FACE_SHAPES = [
  "oval",
  "round",
  "square",
  "heart",
  "diamond",
  "oblong",
] as const;

export const PRODUCT_STATUSES = ["draft", "active", "archived"] as const;

export const ORDER_STATUSES = [
  "pending",
  "paid",
  "in_lab",
  "ready",
  "shipped",
  "delivered",
  "cancelled",
  "refunded",
] as const;
export type OrderStatus = (typeof ORDER_STATUSES)[number];

export const PAYMENT_STATUSES = [
  "unpaid",
  "authorized",
  "paid",
  "partially_refunded",
  "refunded",
  "failed",
] as const;

export const FULFILMENT_STATUSES = [
  "unfulfilled",
  "lab_processing",
  "quality_check",
  "packed",
  "shipped",
  "delivered",
] as const;

export const LAB_STATUSES = [
  "pending",
  "ordered",
  "surfacing",
  "coating",
  "glazing",
  "qc",
  "ready",
] as const;

export const RX_STATUSES = [
  "draft",
  "pending_verification",
  "verified",
  "rejected",
  "expired",
] as const;

export const RX_SOURCES = [
  "manual_entry",
  "uploaded",
  "in_store_exam",
  "imported",
] as const;

/** Lens builder steps, rendered in this order on the product page. */
export const LENS_GROUPS = [
  "usage",
  "type",
  "index",
  "coating",
  "tint",
  "extra",
] as const;
export type LensGroup = (typeof LENS_GROUPS)[number];

export const LENS_GROUP_LABELS: Record<LensGroup, string> = {
  usage: "What are these glasses for?",
  type: "Lens type",
  index: "Lens thickness",
  coating: "Coatings & protection",
  tint: "Tint",
  extra: "Extras",
};

export const LENS_GROUP_HELP: Record<LensGroup, string> = {
  usage: "Tell us how you'll wear them so we fit the right lens.",
  type: "Single vision covers one distance. Progressives blend near and far.",
  index: "Stronger prescriptions look and feel better in a thinner lens.",
  coating: "Anti-reflective and hard coat are recommended on every lens.",
  tint: "Optional colour for sunglasses or light sensitivity.",
  extra: "Finishing touches.",
};

export const PROMOTION_KINDS = [
  "percent_off",
  "amount_off",
  "free_shipping",
  "bogo",
  "free_lens_upgrade",
  "bundle",
] as const;
export type PromotionKind = (typeof PROMOTION_KINDS)[number];

export const PROMOTION_KIND_LABELS: Record<PromotionKind, string> = {
  percent_off: "Percentage off",
  amount_off: "Fixed amount off",
  free_shipping: "Free shipping",
  bogo: "Buy one get one",
  free_lens_upgrade: "Free lens upgrade",
  bundle: "Bundle price",
};

export const PAYMENT_PROVIDERS = ["stripe", "cod", "bank_transfer"] as const;
export type PaymentProviderId = (typeof PAYMENT_PROVIDERS)[number];

export const CARRIERS = [
  "tcs",
  "leopards",
  "dhl",
  "fedex",
  "ups",
  "local",
  "other",
] as const;

export const APPOINTMENT_KINDS = [
  "eye_test",
  "fitting",
  "collection",
  "adjustment",
  "follow_up",
] as const;

export const PATIENT_DOC_KINDS = [
  "prescription_scan",
  "id_document",
  "insurance",
  "photo",
  "other",
] as const;

/** Prism base directions used on the Rx form. */
export const PRISM_BASES = ["in", "out", "up", "down"] as const;

/** Turn `cat_eye` into `Cat eye` for any of the constant lists above. */
export function humanise(value: string | null | undefined): string {
  if (!value) return "";
  const s = value.replace(/_/g, " ");
  return s.charAt(0).toUpperCase() + s.slice(1);
}

/**
 * Sphere/cylinder/add steps in 0.25 D — used to build the Rx dropdowns so a
 * customer can never key in an unfillable value like -2.13.
 */
export function diopterRange(min: number, max: number, step = 0.25): number[] {
  const out: number[] = [];
  for (let v = min; v <= max + 1e-9; v += step) out.push(Math.round(v * 100) / 100);
  return out;
}

export const SPHERE_VALUES = diopterRange(-20, 20);
export const CYLINDER_VALUES = diopterRange(-6, 6);
export const ADD_VALUES = diopterRange(0.75, 3.5);
export const AXIS_VALUES = Array.from({ length: 180 }, (_, i) => i + 1);

/** Signed diopter display: -2.25 stays, 1.5 becomes +1.50. */
export function formatDiopter(v: number | null | undefined): string {
  if (v === null || v === undefined) return "—";
  const sign = v > 0 ? "+" : v < 0 ? "-" : "";
  return `${sign}${Math.abs(v).toFixed(2)}`;
}
