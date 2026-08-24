import { savePromotionAction, deletePromotionAction } from "@/app/actions/admin";
import { fromMinor } from "@/lib/money";
import { PROMOTION_KINDS, PROMOTION_KIND_LABELS } from "@/lib/constants";
import type { Promotion } from "@prisma/client";

/**
 * One form for every kind of deal. `value` carries a different unit per kind —
 * percent for percentage offers, money for everything else — so the hint text
 * next to it changes rather than the field.
 */
export default function PromotionForm({
  promotion,
  brands,
  categories,
}: {
  promotion?: Promotion | null;
  brands: { id: string; name: string }[];
  categories: { id: string; name: string }[];
}) {
  const p = promotion;
  const dt = (d: Date | null | undefined) => (d ? d.toISOString().slice(0, 16) : "");
  const displayValue =
    p == null
      ? ""
      : p.kind === "percent_off"
        ? String(p.value / 100)
        : p.kind === "free_shipping"
          ? ""
          : String(fromMinor(p.value));

  return (
    <div className="space-y-4">
      <form action={savePromotionAction} className="space-y-6">
        {p && <input type="hidden" name="id" value={p.id} />}

        <fieldset className="card p-5">
          <legend className="px-2 text-sm font-semibold">The offer</legend>
          <div className="grid gap-4 sm:grid-cols-2">
            <Text
              name="name"
              label="Name"
              defaultValue={p?.name}
              required
              hint="Shown to customers on the deals page."
            />
            <Text
              name="code"
              label="Code"
              defaultValue={p?.code ?? ""}
              hint="Leave blank to apply automatically, with no code needed."
            />
            <div>
              <label className="label" htmlFor="kind">
                Type
              </label>
              <select id="kind" name="kind" defaultValue={p?.kind ?? "percent_off"} className="field">
                {PROMOTION_KINDS.map((k) => (
                  <option key={k} value={k}>
                    {PROMOTION_KIND_LABELS[k]}
                  </option>
                ))}
              </select>
            </div>
            <Text
              name="value"
              label="Amount"
              type="number"
              step="0.01"
              defaultValue={displayValue}
              hint="Percentage offers: enter 15 for 15%. Everything else: a money amount."
            />
            <Text
              name="maxDiscount"
              label="Cap the discount at"
              type="number"
              step="0.01"
              defaultValue={p?.maxDiscountMinor ? String(fromMinor(p.maxDiscountMinor)) : ""}
              hint="Optional ceiling, useful on percentage offers."
            />
            <Text
              name="priority"
              label="Priority"
              type="number"
              defaultValue={String(p?.priority ?? 0)}
              hint="Higher wins when several deals could apply."
            />
          </div>

          <div className="mt-4">
            <label className="label" htmlFor="description">
              Description
            </label>
            <textarea
              id="description"
              name="description"
              rows={2}
              defaultValue={p?.description ?? ""}
              className="field"
            />
          </div>
        </fieldset>

        <fieldset className="card p-5">
          <legend className="px-2 text-sm font-semibold">When it applies</legend>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <Text
              name="minSubtotal"
              label="Minimum spend"
              type="number"
              step="0.01"
              defaultValue={p?.minSubtotalMinor ? String(fromMinor(p.minSubtotalMinor)) : ""}
            />
            <Text
              name="minQty"
              label="Minimum items"
              type="number"
              defaultValue={String(p?.minQty ?? 1)}
            />
            <Text name="startsAt" label="Starts" type="datetime-local" defaultValue={dt(p?.startsAt)} />
            <Text name="endsAt" label="Ends" type="datetime-local" defaultValue={dt(p?.endsAt)} />
            <Text
              name="usageLimit"
              label="Total uses"
              type="number"
              defaultValue={p?.usageLimit?.toString() ?? ""}
              hint="Blank for unlimited."
            />
            <Text
              name="usageLimitPerUser"
              label="Uses per customer"
              type="number"
              defaultValue={p?.usageLimitPerUser?.toString() ?? ""}
            />
          </div>

          <div className="mt-4 grid gap-4 sm:grid-cols-2">
            <MultiSelect
              name="brandIds"
              label="Only these brands"
              options={brands}
              selected={p?.brandIds ?? ""}
            />
            <MultiSelect
              name="categoryIds"
              label="Only these collections"
              options={categories}
              selected={p?.categoryIds ?? ""}
            />
          </div>
          <p className="mt-2 text-xs text-ink-500">
            Select nothing to apply the deal across the whole catalogue.
          </p>

          <div className="mt-4 space-y-2 text-sm">
            <label className="flex items-center gap-2">
              <input type="checkbox" name="firstOrderOnly" defaultChecked={p?.firstOrderOnly} />
              First order only
            </label>
            <label className="flex items-center gap-2">
              <input type="checkbox" name="stackable" defaultChecked={p?.stackable} />
              Can combine with other stackable deals
            </label>
            <label className="flex items-center gap-2">
              <input type="checkbox" name="isActive" defaultChecked={p?.isActive ?? true} />
              Live
            </label>
          </div>
        </fieldset>

        <fieldset className="card p-5">
          <legend className="px-2 text-sm font-semibold">Storefront banner</legend>
          <div className="grid gap-4 sm:grid-cols-[minmax(0,1fr)_10rem]">
            <Text
              name="bannerText"
              label="Banner text"
              defaultValue={p?.bannerText ?? ""}
              hint="Shown across the top of every page. Leave blank for no banner."
            />
            <div>
              <label className="label" htmlFor="bannerColor">
                Banner colour
              </label>
              <input
                id="bannerColor"
                name="bannerColor"
                type="color"
                defaultValue={p?.bannerColor ?? "#0a67a1"}
                className="h-10 w-full rounded-lg border border-ink-200"
              />
            </div>
          </div>
        </fieldset>

        <button type="submit" className="btn-primary">
          {p ? "Save deal" : "Create deal"}
        </button>
      </form>

      {p && (
        <form action={deletePromotionAction}>
          <input type="hidden" name="id" value={p.id} />
          <button type="submit" className="btn-danger btn-sm">
            Delete deal
          </button>
          <p className="mt-1 text-xs text-ink-500">
            If any order used it, it is deactivated instead so reporting stays correct.
          </p>
        </form>
      )}
    </div>
  );
}

function MultiSelect({
  name,
  label,
  options,
  selected,
}: {
  name: string;
  label: string;
  options: { id: string; name: string }[];
  selected: string;
}) {
  const chosen = selected.split(",").filter(Boolean);
  return (
    <div>
      <label className="label" htmlFor={name}>
        {label}
      </label>
      <select
        id={name}
        name={name}
        multiple
        size={Math.min(6, Math.max(3, options.length))}
        defaultValue={chosen}
        className="field h-auto"
      >
        {options.map((o) => (
          <option key={o.id} value={o.id}>
            {o.name}
          </option>
        ))}
      </select>
    </div>
  );
}

function Text({
  name,
  label,
  type = "text",
  step,
  defaultValue,
  required,
  hint,
}: {
  name: string;
  label: string;
  type?: string;
  step?: string;
  defaultValue?: string;
  required?: boolean;
  hint?: string;
}) {
  return (
    <div>
      <label className="label" htmlFor={name}>
        {label}
      </label>
      <input
        id={name}
        name={name}
        type={type}
        step={step}
        required={required}
        defaultValue={defaultValue}
        className="field"
      />
      {hint && <p className="mt-1 text-xs text-ink-500">{hint}</p>}
    </div>
  );
}
