"use client";

import { useMemo, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { addToCartAction } from "@/app/actions/cart";
import { formatMoney } from "@/lib/money";
import {
  ADD_VALUES,
  AXIS_VALUES,
  CYLINDER_VALUES,
  LENS_GROUPS,
  LENS_GROUP_HELP,
  LENS_GROUP_LABELS,
  SPHERE_VALUES,
  formatDiopter,
  type LensGroup,
} from "@/lib/constants";
import { recommendedIndex } from "@/lib/rx";

/**
 * Turns a frame into an order line: colour, lens build, prescription.
 *
 * Groups that take one answer (usage, type, index, tint) render as cards;
 * coatings and extras are checkboxes. The whole thing is data-driven from the
 * LensOption table, so adding "photochromic" is a row in the back office
 * rather than a change here.
 */

export type BuilderVariant = {
  id: string;
  colorName: string;
  colorHex: string | null;
  priceMinor: number;
  stockQty: number;
  imageUrl: string | null;
};

export type BuilderOption = {
  id: string;
  group: string;
  code: string;
  name: string;
  description: string | null;
  priceMinor: number;
  isDefault: boolean;
  maxSphere: number | null;
  maxCylinder: number | null;
};

export type SavedRx = {
  id: string;
  label: string;
  summary: string;
  status: string;
};

const MULTI_GROUPS = new Set(["coating", "extra"]);

type EyeState = { sphere: string; cylinder: string; axis: string; add: string };
const emptyEye: EyeState = { sphere: "", cylinder: "", axis: "", add: "" };

type Purchase = "prescription" | "non_prescription" | "frame_only";

export default function LensBuilder({
  frameName,
  variants,
  options,
  savedPrescriptions,
  defaultVariantId,
  allowFrameOnly,
  requiresPrescription,
  suggestedPdMm,
}: {
  frameName: string;
  variants: BuilderVariant[];
  options: BuilderOption[];
  savedPrescriptions: SavedRx[];
  defaultVariantId?: string;
  allowFrameOnly: boolean;
  requiresPrescription: boolean;
  suggestedPdMm?: number | null;
}) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();

  const [variantId, setVariantId] = useState(defaultVariantId ?? variants[0]?.id ?? "");
  const [purchase, setPurchase] = useState<Purchase>(
    requiresPrescription || !allowFrameOnly ? "prescription" : "prescription",
  );
  const [single, setSingle] = useState<Record<string, string>>(() => defaultSingles(options));
  const [multi, setMulti] = useState<Set<string>>(() => defaultMultis(options));

  const [rxSource, setRxSource] = useState<"type" | "saved" | "later">(
    savedPrescriptions.length ? "saved" : "type",
  );
  const [savedRxId, setSavedRxId] = useState(savedPrescriptions[0]?.id ?? "");
  const [od, setOd] = useState<EyeState>(emptyEye);
  const [os, setOs] = useState<EyeState>(emptyEye);
  const [pd, setPd] = useState(suggestedPdMm ? String(suggestedPdMm) : "");

  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  const variant = variants.find((v) => v.id === variantId) ?? variants[0];

  const activeGroups = useMemo(
    () =>
      LENS_GROUPS.filter((g) =>
        purchase === "frame_only" ? false : options.some((o) => o.group === g),
      ),
    [options, purchase],
  );

  const selectedCodes = useMemo(() => {
    if (purchase === "frame_only") return [];
    return [...Object.values(single).filter(Boolean), ...multi];
  }, [single, multi, purchase]);

  const lensTotal = useMemo(
    () =>
      options
        .filter((o) => selectedCodes.includes(o.code))
        .reduce((s, o) => s + o.priceMinor, 0),
    [options, selectedCodes],
  );

  const framePrice = variant?.priceMinor ?? 0;
  const total = framePrice + lensTotal;

  // Warn about a lens that is too thin for the prescription while the customer
  // can still change it, rather than at the till.
  const indexAdvice = useMemo(() => {
    if (purchase !== "prescription" || rxSource !== "type") return null;

    const rx = {
      odSphere: num(od.sphere),
      odCylinder: num(od.cylinder),
      osSphere: num(os.sphere),
      osCylinder: num(os.cylinder),
    };
    if (!rx.odSphere && !rx.osSphere) return null;

    const want = recommendedIndex(rx);
    const chosen = single.index;
    if (!chosen) return `For your prescription we'd suggest the ${want} index lens.`;

    const chosenOpt = options.find((o) => o.code === chosen);
    if (chosenOpt && !chosenOpt.name.includes(want)) {
      return `With this prescription the ${want} index gives a noticeably thinner lens.`;
    }
    return null;
  }, [purchase, rxSource, od.sphere, od.cylinder, os.sphere, os.cylinder, single.index, options]);

  function toggleMulti(code: string) {
    setMulti((prev) => {
      const next = new Set(prev);
      if (next.has(code)) next.delete(code);
      else next.add(code);
      return next;
    });
  }

  function submit() {
    setError(null);

    if (purchase === "prescription" && rxSource === "type") {
      if (!od.sphere && !os.sphere && !od.cylinder && !os.cylinder) {
        setError("Enter your prescription, choose a saved one, or send it to us later.");
        return;
      }
      if (!pd) {
        setError("We need your pupillary distance. Measure it in the virtual try-on if unsure.");
        return;
      }
    }

    const payload = {
      variantId,
      qty: 1,
      lensOptionCodes: selectedCodes,
      ...(purchase === "prescription" && rxSource === "type"
        ? {
            prescription: {
              od: eyeToApi(od),
              os: eyeToApi(os),
              pdMm: num(pd),
            },
          }
        : {}),
      ...(purchase === "prescription" && rxSource === "saved" && savedRxId
        ? { prescriptionId: savedRxId }
        : {}),
    };

    startTransition(async () => {
      const res = await addToCartAction(payload);
      if (res.ok) {
        setDone(true);
        router.refresh();
      } else {
        setError(res.error);
      }
    });
  }

  return (
    <div className="space-y-8">
      {/* Colour */}
      <section>
        <h3 className="text-sm font-semibold">
          Colour: <span className="font-normal text-ink-600">{variant?.colorName}</span>
        </h3>
        <div className="mt-3 flex flex-wrap gap-2">
          {variants.map((v) => (
            <button
              key={v.id}
              type="button"
              onClick={() => setVariantId(v.id)}
              title={`${v.colorName}${v.stockQty <= 0 ? " — out of stock" : ""}`}
              disabled={v.stockQty <= 0}
              className={`flex items-center gap-2 rounded-lg border px-3 py-2 text-sm transition disabled:opacity-40 ${
                v.id === variantId ? "border-ink-900 ring-1 ring-ink-900" : "border-ink-200 hover:border-ink-400"
              }`}
            >
              <span
                className="h-4 w-4 rounded-full border border-ink-300"
                style={{ background: v.colorHex || "#ccc" }}
              />
              {v.colorName}
            </button>
          ))}
        </div>
      </section>

      {/* What are we making */}
      <section>
        <h3 className="text-sm font-semibold">What do you need?</h3>
        <div className="mt-3 grid gap-2 sm:grid-cols-3">
          <ChoiceCard
            selected={purchase === "prescription"}
            onSelect={() => setPurchase("prescription")}
            title="Prescription lenses"
            body="Made to your Rx."
          />
          <ChoiceCard
            selected={purchase === "non_prescription"}
            onSelect={() => setPurchase("non_prescription")}
            title="Plain lenses"
            body="No correction — style or screen use."
            disabled={requiresPrescription}
          />
          <ChoiceCard
            selected={purchase === "frame_only"}
            onSelect={() => setPurchase("frame_only")}
            title="Frame only"
            body="You'll fit your own lenses."
            disabled={!allowFrameOnly || requiresPrescription}
          />
        </div>
      </section>

      {/* Lens options */}
      {activeGroups.map((group) => (
        <LensGroupSection
          key={group}
          group={group}
          options={options.filter((o) => o.group === group)}
          multi={MULTI_GROUPS.has(group)}
          singleValue={single[group]}
          multiValues={multi}
          onSingle={(code) => setSingle((s) => ({ ...s, [group]: code }))}
          onToggle={toggleMulti}
        />
      ))}

      {indexAdvice && (
        <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">{indexAdvice}</p>
      )}

      {/* Prescription */}
      {purchase === "prescription" && (
        <section className="card p-5">
          <h3 className="font-semibold">Your prescription</h3>

          <div className="mt-3 flex flex-wrap gap-2 text-sm">
            {savedPrescriptions.length > 0 && (
              <RadioPill
                checked={rxSource === "saved"}
                onChange={() => setRxSource("saved")}
                label="Use a saved prescription"
              />
            )}
            <RadioPill
              checked={rxSource === "type"}
              onChange={() => setRxSource("type")}
              label="Type it in"
            />
            <RadioPill
              checked={rxSource === "later"}
              onChange={() => setRxSource("later")}
              label="Send it to us later"
            />
          </div>

          {rxSource === "saved" && (
            <div className="mt-4 space-y-2">
              {savedPrescriptions.map((rx) => (
                <label
                  key={rx.id}
                  className={`flex cursor-pointer items-start gap-3 rounded-lg border p-3 ${
                    savedRxId === rx.id ? "border-ink-900 bg-ink-50" : "border-ink-200"
                  }`}
                >
                  <input
                    type="radio"
                    name="savedRx"
                    className="mt-1"
                    checked={savedRxId === rx.id}
                    onChange={() => setSavedRxId(rx.id)}
                  />
                  <span>
                    <span className="block text-sm font-medium">{rx.label}</span>
                    <span className="block font-mono text-xs text-ink-600">{rx.summary}</span>
                    {rx.status !== "verified" && (
                      <span className="mt-1 inline-block text-xs text-amber-700">
                        Awaiting optician check
                      </span>
                    )}
                  </span>
                </label>
              ))}
            </div>
          )}

          {rxSource === "type" && (
            <div className="mt-4 space-y-4">
              <div className="table-wrap">
                <table className="table min-w-[34rem]">
                  <thead>
                    <tr>
                      <th>Eye</th>
                      <th>Sphere (SPH)</th>
                      <th>Cylinder (CYL)</th>
                      <th>Axis</th>
                      <th>Add</th>
                    </tr>
                  </thead>
                  <tbody>
                    <EyeRow label="Right (OD)" value={od} onChange={setOd} />
                    <EyeRow label="Left (OS)" value={os} onChange={setOs} />
                  </tbody>
                </table>
              </div>

              <div className="grid gap-3 sm:grid-cols-2">
                <div>
                  <label className="label" htmlFor="pd">
                    Pupillary distance (mm)
                  </label>
                  <input
                    id="pd"
                    inputMode="decimal"
                    value={pd}
                    onChange={(e) => setPd(e.target.value)}
                    placeholder="e.g. 63"
                    className="field"
                  />
                  <p className="mt-1 text-xs text-ink-500">
                    Don&apos;t know it? The{" "}
                    <a href="/try-on" className="text-brand-600 underline">
                      virtual try-on
                    </a>{" "}
                    measures it from your photo.
                  </p>
                </div>
              </div>

              <p className="text-xs text-ink-500">
                Values step in 0.25 D, exactly as they appear on your paper prescription. Our
                optician checks every prescription before lenses are cut.
              </p>
            </div>
          )}

          {rxSource === "later" && (
            <p className="mt-4 rounded-lg bg-brand-50 px-3 py-2 text-sm text-brand-700">
              No problem — we&apos;ll email you a secure link after checkout where you can upload a
              photo of your prescription. Your order waits in the lab queue until it arrives.
            </p>
          )}
        </section>
      )}

      {/* Price + add */}
      <section className="card sticky bottom-4 p-5 shadow-lg">
        <dl className="space-y-1.5 text-sm">
          <div className="flex justify-between">
            <dt className="text-ink-600">{frameName} frame</dt>
            <dd>{formatMoney(framePrice)}</dd>
          </div>
          {purchase !== "frame_only" && (
            <div className="flex justify-between">
              <dt className="text-ink-600">Lenses &amp; coatings</dt>
              <dd>{lensTotal === 0 ? "Included" : formatMoney(lensTotal)}</dd>
            </div>
          )}
          <div className="flex justify-between border-t border-ink-200 pt-2 text-base font-semibold">
            <dt>Total</dt>
            <dd>{formatMoney(total)}</dd>
          </div>
        </dl>

        {error && (
          <p className="mt-3 rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700" role="alert">
            {error}
          </p>
        )}

        {done ? (
          <div className="mt-4 flex gap-2">
            <a href="/cart" className="btn-primary flex-1">
              Go to bag
            </a>
            <button type="button" onClick={() => setDone(false)} className="btn-secondary">
              Keep shopping
            </button>
          </div>
        ) : (
          <button
            type="button"
            onClick={submit}
            disabled={pending || !variant || variant.stockQty <= 0}
            className="btn-primary mt-4 w-full py-3 text-base"
          >
            {pending ? "Adding…" : variant && variant.stockQty > 0 ? "Add to bag" : "Out of stock"}
          </button>
        )}
      </section>
    </div>
  );
}

// --- Pieces ---------------------------------------------------------------

function LensGroupSection({
  group,
  options,
  multi,
  singleValue,
  multiValues,
  onSingle,
  onToggle,
}: {
  group: LensGroup;
  options: BuilderOption[];
  multi: boolean;
  singleValue?: string;
  multiValues: Set<string>;
  onSingle: (code: string) => void;
  onToggle: (code: string) => void;
}) {
  if (options.length === 0) return null;
  return (
    <section>
      <h3 className="text-sm font-semibold">{LENS_GROUP_LABELS[group]}</h3>
      <p className="mt-0.5 text-xs text-ink-500">{LENS_GROUP_HELP[group]}</p>
      <div className="mt-3 grid gap-2 sm:grid-cols-2">
        {options.map((o) => {
          const selected = multi ? multiValues.has(o.code) : singleValue === o.code;
          return (
            <button
              key={o.id}
              type="button"
              onClick={() => (multi ? onToggle(o.code) : onSingle(o.code))}
              className={`rounded-xl border p-3 text-left transition ${
                selected ? "border-ink-900 bg-ink-50 ring-1 ring-ink-900" : "border-ink-200 hover:border-ink-400"
              }`}
            >
              <div className="flex items-baseline justify-between gap-3">
                <span className="text-sm font-medium">{o.name}</span>
                <span className="shrink-0 text-sm">
                  {o.priceMinor === 0 ? "Included" : `+${formatMoney(o.priceMinor)}`}
                </span>
              </div>
              {o.description && <p className="mt-1 text-xs text-ink-500">{o.description}</p>}
            </button>
          );
        })}
      </div>
    </section>
  );
}

function ChoiceCard({
  selected,
  onSelect,
  title,
  body,
  disabled,
}: {
  selected: boolean;
  onSelect: () => void;
  title: string;
  body: string;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onSelect}
      disabled={disabled}
      className={`rounded-xl border p-3 text-left transition disabled:cursor-not-allowed disabled:opacity-40 ${
        selected ? "border-ink-900 bg-ink-50 ring-1 ring-ink-900" : "border-ink-200 hover:border-ink-400"
      }`}
    >
      <span className="block text-sm font-medium">{title}</span>
      <span className="mt-0.5 block text-xs text-ink-500">{body}</span>
    </button>
  );
}

function RadioPill({
  checked,
  onChange,
  label,
}: {
  checked: boolean;
  onChange: () => void;
  label: string;
}) {
  return (
    <label
      className={`cursor-pointer rounded-full border px-3 py-1.5 ${
        checked ? "border-ink-900 bg-ink-900 text-white" : "border-ink-200 hover:bg-ink-50"
      }`}
    >
      <input type="radio" checked={checked} onChange={onChange} className="sr-only" />
      {label}
    </label>
  );
}

function EyeRow({
  label,
  value,
  onChange,
}: {
  label: string;
  value: EyeState;
  onChange: (v: EyeState) => void;
}) {
  return (
    <tr>
      <td className="font-medium whitespace-nowrap">{label}</td>
      <td>
        <DiopterSelect
          values={SPHERE_VALUES}
          value={value.sphere}
          onChange={(sphere) => onChange({ ...value, sphere })}
        />
      </td>
      <td>
        <DiopterSelect
          values={CYLINDER_VALUES}
          value={value.cylinder}
          onChange={(cylinder) => onChange({ ...value, cylinder })}
        />
      </td>
      <td>
        <select
          className="field py-1.5"
          value={value.axis}
          onChange={(e) => onChange({ ...value, axis: e.target.value })}
        >
          <option value="">—</option>
          {AXIS_VALUES.map((a) => (
            <option key={a} value={a}>
              {a}°
            </option>
          ))}
        </select>
      </td>
      <td>
        <DiopterSelect
          values={ADD_VALUES}
          value={value.add}
          onChange={(add) => onChange({ ...value, add })}
        />
      </td>
    </tr>
  );
}

function DiopterSelect({
  values,
  value,
  onChange,
}: {
  values: number[];
  value: string;
  onChange: (v: string) => void;
}) {
  return (
    <select className="field py-1.5" value={value} onChange={(e) => onChange(e.target.value)}>
      <option value="">—</option>
      {values.map((v) => (
        <option key={v} value={v}>
          {formatDiopter(v)}
        </option>
      ))}
    </select>
  );
}

// --- helpers --------------------------------------------------------------

function defaultSingles(options: BuilderOption[]): Record<string, string> {
  const out: Record<string, string> = {};
  for (const o of options) {
    if (MULTI_GROUPS.has(o.group)) continue;
    if (o.isDefault && !out[o.group]) out[o.group] = o.code;
  }
  return out;
}

function defaultMultis(options: BuilderOption[]): Set<string> {
  return new Set(options.filter((o) => MULTI_GROUPS.has(o.group) && o.isDefault).map((o) => o.code));
}

function num(v: string): number | null {
  if (v === "" || v == null) return null;
  const n = Number(v);
  return Number.isFinite(n) ? n : null;
}

function eyeToApi(e: EyeState) {
  return {
    sphere: num(e.sphere),
    cylinder: num(e.cylinder),
    axis: e.axis ? Number(e.axis) : null,
    add: num(e.add),
  };
}
