import { humanise } from "@/lib/constants";

/** One colour language for every status string in the app. */
const TONES: Record<string, string> = {
  // good
  verified: "bg-emerald-100 text-emerald-800",
  delivered: "bg-emerald-100 text-emerald-800",
  paid: "bg-emerald-100 text-emerald-800",
  succeeded: "bg-emerald-100 text-emerald-800",
  ready: "bg-emerald-100 text-emerald-800",
  active: "bg-emerald-100 text-emerald-800",
  // bad
  rejected: "bg-rose-100 text-rose-800",
  cancelled: "bg-rose-100 text-rose-800",
  expired: "bg-rose-100 text-rose-800",
  failed: "bg-rose-100 text-rose-800",
  refunded: "bg-rose-100 text-rose-800",
  // waiting
  pending: "bg-amber-100 text-amber-800",
  draft: "bg-amber-100 text-amber-800",
  pending_verification: "bg-amber-100 text-amber-800",
  unpaid: "bg-amber-100 text-amber-800",
  unfulfilled: "bg-amber-100 text-amber-800",
  // in flight
  in_lab: "bg-brand-100 text-brand-700",
  lab_processing: "bg-brand-100 text-brand-700",
  shipped: "bg-brand-100 text-brand-700",
  in_transit: "bg-brand-100 text-brand-700",
};

export default function StatusChip({ status }: { status: string }) {
  return (
    <span className={`chip ${TONES[status] ?? "bg-ink-100 text-ink-700"}`}>
      {humanise(status)}
    </span>
  );
}
