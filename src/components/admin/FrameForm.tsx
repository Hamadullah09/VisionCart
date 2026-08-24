import { saveFrameAction } from "@/app/actions/admin";
import { fromMinor } from "@/lib/money";
import {
  FACE_SHAPES,
  FRAME_MATERIALS,
  FRAME_SHAPES,
  GENDERS,
  PRODUCT_STATUSES,
  RIM_TYPES,
  humanise,
} from "@/lib/constants";
import type { Frame } from "@prisma/client";

/**
 * The frame record itself. Colourways, stock and images hang off it and are
 * edited separately — this form is only the things that are true of the model
 * regardless of colour.
 */
export default function FrameForm({
  frame,
  brands,
}: {
  frame?: Frame | null;
  brands: { id: string; name: string }[];
}) {
  const major = (minor: number | null | undefined) =>
    minor == null ? "" : String(fromMinor(minor));

  return (
    <form action={saveFrameAction} className="space-y-6">
      {frame && <input type="hidden" name="id" value={frame.id} />}

      <fieldset className="card p-5">
        <legend className="px-2 text-sm font-semibold">Basics</legend>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <Text name="name" label="Frame name" defaultValue={frame?.name} required />
          <Text
            name="sku"
            label="SKU"
            defaultValue={frame?.sku}
            hint="Left blank, we'll generate one."
          />
          <Text
            name="slug"
            label="URL slug"
            defaultValue={frame?.slug}
            hint="Left blank, we'll build it from the name."
          />
          <Select
            name="brandId"
            label="Brand"
            defaultValue={frame?.brandId ?? ""}
            options={brands.map((b) => [b.id, b.name])}
            placeholder="No brand"
          />
          <Select
            name="status"
            label="Status"
            defaultValue={frame?.status ?? "draft"}
            options={PRODUCT_STATUSES.map((s) => [s, humanise(s)])}
          />
          <Text
            name="position"
            label="Sort order"
            type="number"
            defaultValue={String(frame?.position ?? 0)}
          />
        </div>

        <div className="mt-4">
          <label className="label" htmlFor="description">
            Description
          </label>
          <textarea
            id="description"
            name="description"
            rows={3}
            defaultValue={frame?.description ?? ""}
            className="field"
            placeholder="How they wear, who they suit, what they're made of."
          />
        </div>
      </fieldset>

      <fieldset className="card p-5">
        <legend className="px-2 text-sm font-semibold">Style</legend>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <Select
            name="shape"
            label="Shape"
            defaultValue={frame?.shape ?? ""}
            options={FRAME_SHAPES.map((s) => [s, humanise(s)])}
            placeholder="—"
          />
          <Select
            name="material"
            label="Material"
            defaultValue={frame?.material ?? ""}
            options={FRAME_MATERIALS.map((m) => [m, humanise(m)])}
            placeholder="—"
          />
          <Select
            name="rimType"
            label="Rim"
            defaultValue={frame?.rimType ?? "full_rim"}
            options={RIM_TYPES.map((r) => [r, humanise(r)])}
          />
          <Select
            name="gender"
            label="Wearer"
            defaultValue={frame?.gender ?? "unisex"}
            options={GENDERS.map((g) => [g, humanise(g)])}
          />
        </div>

        <div className="mt-4">
          <p className="label">Suits these face shapes</p>
          <div className="flex flex-wrap gap-3">
            {FACE_SHAPES.map((s) => (
              <label key={s} className="flex items-center gap-1.5 text-sm">
                <input
                  type="checkbox"
                  name="faceShapes"
                  value={s}
                  defaultChecked={frame?.faceShapes?.split(",").includes(s)}
                />
                {humanise(s)}
              </label>
            ))}
          </div>
          <p className="mt-1 text-xs text-ink-500">
            Used to recommend frames after a try-on measures the customer&apos;s face.
          </p>
        </div>
      </fieldset>

      <fieldset className="card p-5">
        <legend className="px-2 text-sm font-semibold">Measurements (mm)</legend>
        <div className="grid gap-4 sm:grid-cols-3 lg:grid-cols-6">
          <Text
            name="lensWidthMm"
            label="Lens width"
            type="number"
            step="0.5"
            defaultValue={frame?.lensWidthMm?.toString()}
          />
          <Text
            name="bridgeWidthMm"
            label="Bridge"
            type="number"
            step="0.5"
            defaultValue={frame?.bridgeWidthMm?.toString()}
          />
          <Text
            name="templeLengthMm"
            label="Temple"
            type="number"
            step="0.5"
            defaultValue={frame?.templeLengthMm?.toString()}
          />
          <Text
            name="lensHeightMm"
            label="Lens height"
            type="number"
            step="0.5"
            defaultValue={frame?.lensHeightMm?.toString()}
          />
          <Text
            name="totalWidthMm"
            label="Total width"
            type="number"
            step="0.5"
            defaultValue={frame?.totalWidthMm?.toString()}
            hint="Sets the size band."
          />
          <Text
            name="weightGrams"
            label="Weight (g)"
            type="number"
            step="0.1"
            defaultValue={frame?.weightGrams?.toString()}
          />
        </div>
      </fieldset>

      <fieldset className="card p-5">
        <legend className="px-2 text-sm font-semibold">Price &amp; rules</legend>
        <div className="grid gap-4 sm:grid-cols-3">
          <Text
            name="basePrice"
            label="Frame price"
            type="number"
            step="0.01"
            required
            defaultValue={major(frame?.basePriceMinor)}
          />
          <Text
            name="compareAt"
            label="Was price"
            type="number"
            step="0.01"
            defaultValue={major(frame?.compareAtMinor)}
            hint="Shows a strikethrough."
          />
          <Text
            name="cost"
            label="Cost to us"
            type="number"
            step="0.01"
            defaultValue={major(frame?.costMinor)}
            hint="Internal only."
          />
        </div>

        <div className="mt-4 space-y-2 text-sm">
          <label className="flex items-center gap-2">
            <input type="checkbox" name="isFeatured" defaultChecked={frame?.isFeatured} />
            Feature on the home page
          </label>
          <label className="flex items-center gap-2">
            <input
              type="checkbox"
              name="allowFrameOnly"
              defaultChecked={frame?.allowFrameOnly ?? true}
            />
            Can be bought without lenses
          </label>
          <label className="flex items-center gap-2">
            <input
              type="checkbox"
              name="requiresPrescription"
              defaultChecked={frame?.requiresPrescription}
            />
            Prescription lenses only
          </label>
        </div>
      </fieldset>

      <fieldset className="card p-5">
        <legend className="px-2 text-sm font-semibold">Search listing</legend>
        <div className="grid gap-4 sm:grid-cols-2">
          <Text name="metaTitle" label="Page title" defaultValue={frame?.metaTitle ?? ""} />
          <Text name="metaDesc" label="Meta description" defaultValue={frame?.metaDesc ?? ""} />
        </div>
      </fieldset>

      <div className="flex gap-3">
        <button type="submit" className="btn-primary">
          {frame ? "Save frame" : "Create frame"}
        </button>
      </div>
    </form>
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

function Select({
  name,
  label,
  defaultValue,
  options,
  placeholder,
}: {
  name: string;
  label: string;
  defaultValue?: string;
  options: [string, string][];
  placeholder?: string;
}) {
  return (
    <div>
      <label className="label" htmlFor={name}>
        {label}
      </label>
      <select id={name} name={name} defaultValue={defaultValue} className="field">
        {placeholder && <option value="">{placeholder}</option>}
        {options.map(([v, l]) => (
          <option key={v} value={v}>
            {l}
          </option>
        ))}
      </select>
    </div>
  );
}
