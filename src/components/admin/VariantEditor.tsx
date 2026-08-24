"use client";

import { useActionState, useState } from "react";
import { saveVariantAction, type AdminResult } from "@/app/actions/admin";
import BulkUploader from "./BulkUploader";
import TryOnAnchorTuner from "./TryOnAnchorTuner";

export type EditableVariant = {
  id: string;
  frameId: string;
  sku: string;
  colorName: string;
  colorHex: string | null;
  barcode: string | null;
  priceMajor: string;
  stockQty: number;
  lowStockAt: number;
  isActive: boolean;
  position: number;
  tryOnImageUrl: string | null;
  anchorLeftX: number;
  anchorLeftY: number;
  anchorRightX: number;
  anchorRightY: number;
  tryOnScaleAdj: number;
  tryOnOpacity: number;
  images: { id: string; url: string; thumbUrl: string | null; role: string }[];
};

export default function VariantEditor({
  variant,
  frameId,
  onDeleted,
}: {
  variant: EditableVariant | null;
  frameId: string;
  onDeleted?: () => void;
}) {
  const [state, action, pending] = useActionState<AdminResult | null, FormData>(
    saveVariantAction,
    null,
  );
  const [open, setOpen] = useState(!variant);
  const [tab, setTab] = useState<"details" | "images" | "tryon">("details");

  const v = variant;

  return (
    <div className="card overflow-hidden">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="flex w-full items-center gap-3 p-4 text-left hover:bg-ink-50"
      >
        <span
          className="h-6 w-6 shrink-0 rounded-full border border-ink-300"
          style={{ background: v?.colorHex || "#ccc" }}
        />
        <span className="min-w-0 flex-1">
          <span className="block font-medium">{v?.colorName ?? "New colourway"}</span>
          <span className="block text-xs text-ink-500">
            {v ? `${v.sku} · ${v.stockQty} in stock` : "Add a colour, stock and images"}
          </span>
        </span>
        {v && !v.isActive && <span className="chip bg-ink-100 text-ink-700">Hidden</span>}
        {v?.tryOnImageUrl && <span className="chip bg-brand-100 text-brand-700">Try-on ready</span>}
        <span className="text-ink-400">{open ? "▲" : "▼"}</span>
      </button>

      {open && (
        <div className="border-t border-ink-200 p-4">
          {v && (
            <div className="mb-4 flex gap-1 border-b border-ink-200">
              {(["details", "images", "tryon"] as const).map((t) => (
                <button
                  key={t}
                  type="button"
                  onClick={() => setTab(t)}
                  className={`px-3 py-2 text-sm font-medium ${
                    tab === t
                      ? "border-b-2 border-ink-900 text-ink-900"
                      : "text-ink-500 hover:text-ink-800"
                  }`}
                >
                  {t === "details" ? "Details" : t === "images" ? "Photos" : "Virtual try-on"}
                </button>
              ))}
            </div>
          )}

          {(!v || tab === "details") && (
            <form action={action} className="space-y-4">
              <input type="hidden" name="frameId" value={frameId} />
              {v && <input type="hidden" name="id" value={v.id} />}
              {/* Anchors are edited on the try-on tab; carry them through so a
                  details save doesn't reset them to the defaults. */}
              <input type="hidden" name="anchorLeftX" value={v?.anchorLeftX ?? 0.29} />
              <input type="hidden" name="anchorLeftY" value={v?.anchorLeftY ?? 0.5} />
              <input type="hidden" name="anchorRightX" value={v?.anchorRightX ?? 0.71} />
              <input type="hidden" name="anchorRightY" value={v?.anchorRightY ?? 0.5} />
              <input type="hidden" name="tryOnScaleAdj" value={v?.tryOnScaleAdj ?? 1} />
              <input type="hidden" name="tryOnOpacity" value={v?.tryOnOpacity ?? 1} />

              <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                <Field name="colorName" label="Colour name" defaultValue={v?.colorName} required />
                <div>
                  <label className="label" htmlFor={`hex-${v?.id ?? "new"}`}>
                    Swatch
                  </label>
                  <input
                    id={`hex-${v?.id ?? "new"}`}
                    name="colorHex"
                    type="color"
                    defaultValue={v?.colorHex ?? "#333333"}
                    className="h-10 w-full rounded-lg border border-ink-200"
                  />
                </div>
                <Field name="sku" label="SKU" defaultValue={v?.sku} required />
                <Field name="barcode" label="Barcode" defaultValue={v?.barcode ?? ""} />
                <Field
                  name="price"
                  label="Price override"
                  type="number"
                  step="0.01"
                  defaultValue={v?.priceMajor}
                  hint="Leave blank to use the frame price."
                />
                <Field
                  name="stockQty"
                  label="In stock"
                  type="number"
                  defaultValue={String(v?.stockQty ?? 0)}
                />
                <Field
                  name="lowStockAt"
                  label="Warn at"
                  type="number"
                  defaultValue={String(v?.lowStockAt ?? 3)}
                />
                <Field
                  name="position"
                  label="Sort order"
                  type="number"
                  defaultValue={String(v?.position ?? 0)}
                />
              </div>

              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" name="isActive" defaultChecked={v?.isActive ?? true} />
                Show this colour in the shop
              </label>

              {state && !state.ok && (
                <p className="rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700">
                  {state.error}
                </p>
              )}
              {state?.ok && state.message && (
                <p className="rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-800">
                  {state.message}
                </p>
              )}

              <div className="flex gap-2">
                <button type="submit" disabled={pending} className="btn-primary btn-sm">
                  {pending ? "Saving…" : v ? "Save colourway" : "Add colourway"}
                </button>
                {onDeleted && v && (
                  <button type="button" onClick={onDeleted} className="btn-danger btn-sm">
                    Remove
                  </button>
                )}
              </div>
            </form>
          )}

          {v && tab === "images" && (
            <div className="space-y-4">
              {v.images.length > 0 && (
                <div className="grid grid-cols-3 gap-2 sm:grid-cols-6">
                  {v.images.map((img) => (
                    <div key={img.id} className="card overflow-hidden p-1">
                      {/* eslint-disable-next-line @next/next/no-img-element */}
                      <img
                        src={img.thumbUrl ?? img.url}
                        alt=""
                        className="aspect-square w-full bg-ink-50 object-contain"
                      />
                      <p className="truncate text-center text-[11px] text-ink-500">{img.role}</p>
                    </div>
                  ))}
                </div>
              )}
              <BulkUploader
                variantId={v.id}
                role="gallery"
                label={`Drop photos of ${v.colorName}`}
                hint="The first one becomes the main catalogue image."
              />
            </div>
          )}

          {v && tab === "tryon" && (
            <TryOnAnchorTuner
              variantId={v.id}
              frameId={frameId}
              overlayUrl={v.tryOnImageUrl}
              anchors={{
                leftX: v.anchorLeftX,
                leftY: v.anchorLeftY,
                rightX: v.anchorRightX,
                rightY: v.anchorRightY,
                scaleAdj: v.tryOnScaleAdj,
              }}
              opacity={v.tryOnOpacity}
              variant={v}
            />
          )}
        </div>
      )}
    </div>
  );
}

function Field({
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
      <label className="label" htmlFor={`${name}-field`}>
        {label}
      </label>
      <input
        id={`${name}-field`}
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
