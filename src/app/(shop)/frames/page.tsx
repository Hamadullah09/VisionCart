import Link from "next/link";
import { getFacets, listFrames, type FrameFilters } from "@/lib/catalog";
import { FRAME_SHAPES, GENDERS, RIM_TYPES, humanise } from "@/lib/constants";
import ProductCard from "@/components/shop/ProductCard";

export const metadata = { title: "All frames" };

export default async function FramesPage({ searchParams }: PageProps<"/frames">) {
  const sp = await searchParams;
  const filters = parseFilters(sp);

  const [result, facets] = await Promise.all([listFrames(filters), getFacets()]);

  return (
    <div className="mx-auto max-w-7xl px-4 py-10">
      <header className="mb-8">
        <h1 className="text-3xl font-semibold">
          {filters.gender ? `${humanise(filters.gender)}'s frames` : "All frames"}
        </h1>
        <p className="mt-1 text-sm text-ink-600">
          {result.total} frame{result.total === 1 ? "" : "s"} — every one can be tried on before you
          buy.
        </p>
      </header>

      <div className="grid gap-8 lg:grid-cols-[240px_minmax(0,1fr)]">
        {/* Filters — a plain GET form, so it works without JavaScript and every
            filtered view has a shareable URL. */}
        <form method="get" className="space-y-5 lg:sticky lg:top-24 lg:self-start">
          <div>
            <label className="label" htmlFor="q">
              Search
            </label>
            <input
              id="q"
              name="q"
              defaultValue={filters.q ?? ""}
              placeholder="Name, brand or SKU"
              className="field"
            />
          </div>

          <SelectFilter
            name="gender"
            label="Wearer"
            value={filters.gender}
            options={GENDERS.map((g) => [g, humanise(g)])}
          />
          <SelectFilter
            name="shape"
            label="Shape"
            value={filters.shape}
            options={FRAME_SHAPES.map((s) => [s, humanise(s)])}
          />
          <SelectFilter
            name="rimType"
            label="Rim"
            value={filters.rimType}
            options={RIM_TYPES.map((r) => [r, humanise(r)])}
          />
          <SelectFilter
            name="brand"
            label="Brand"
            value={filters.brand}
            options={facets.brands.map((b) => [b.slug, b.name])}
          />
          <SelectFilter
            name="category"
            label="Collection"
            value={filters.category}
            options={facets.categories.map((c) => [c.slug, c.name])}
          />
          <SelectFilter
            name="sizeBand"
            label="Frame size"
            value={filters.sizeBand}
            options={[
              ["narrow", "Narrow"],
              ["medium", "Medium"],
              ["wide", "Wide"],
            ]}
          />
          <SelectFilter
            name="sort"
            label="Sort by"
            value={filters.sort}
            placeholder="Featured"
            options={[
              ["price_asc", "Price: low to high"],
              ["price_desc", "Price: high to low"],
              ["newest", "Newest first"],
            ]}
          />

          <div className="flex gap-2">
            <button type="submit" className="btn-primary flex-1">
              Apply
            </button>
            <Link href="/frames" className="btn-secondary">
              Clear
            </Link>
          </div>
        </form>

        <div>
          {result.items.length === 0 ? (
            <div className="card p-10 text-center">
              <p className="font-medium">No frames match those filters.</p>
              <p className="mt-1 text-sm text-ink-600">
                Try widening your search — or{" "}
                <Link href="/try-on" className="text-brand-600 underline">
                  find your face shape
                </Link>{" "}
                and let us suggest some.
              </p>
            </div>
          ) : (
            <div className="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">
              {result.items.map((f) => (
                <ProductCard key={f.id} frame={f} />
              ))}
            </div>
          )}

          {result.pages > 1 && (
            <nav className="mt-10 flex justify-center gap-1.5">
              {Array.from({ length: result.pages }, (_, i) => i + 1).map((p) => (
                <Link
                  key={p}
                  href={`/frames?${withPage(sp, p)}`}
                  className={`rounded-lg border px-3.5 py-2 text-sm ${
                    p === result.page
                      ? "border-ink-900 bg-ink-900 text-white"
                      : "border-ink-200 hover:bg-ink-50"
                  }`}
                >
                  {p}
                </Link>
              ))}
            </nav>
          )}
        </div>
      </div>
    </div>
  );
}

function SelectFilter({
  name,
  label,
  value,
  options,
  placeholder = "Any",
}: {
  name: string;
  label: string;
  value?: string;
  options: [string, string][];
  placeholder?: string;
}) {
  return (
    <div>
      <label className="label" htmlFor={name}>
        {label}
      </label>
      <select id={name} name={name} defaultValue={value ?? ""} className="field">
        <option value="">{placeholder}</option>
        {options.map(([v, l]) => (
          <option key={v} value={v}>
            {l}
          </option>
        ))}
      </select>
    </div>
  );
}

type SP = Record<string, string | string[] | undefined>;

function one(sp: SP, key: string): string | undefined {
  const v = sp[key];
  const s = Array.isArray(v) ? v[0] : v;
  return s && s.length ? s : undefined;
}

function parseFilters(sp: SP): FrameFilters {
  const page = Number(one(sp, "page") ?? 1);
  const sort = one(sp, "sort");
  return {
    q: one(sp, "q"),
    gender: one(sp, "gender"),
    shape: one(sp, "shape"),
    material: one(sp, "material"),
    rimType: one(sp, "rimType"),
    brand: one(sp, "brand"),
    category: one(sp, "category"),
    faceShape: one(sp, "faceShape"),
    sizeBand: one(sp, "sizeBand"),
    sort: (["price_asc", "price_desc", "newest", "featured"] as const).includes(sort as never)
      ? (sort as FrameFilters["sort"])
      : undefined,
    page: Number.isFinite(page) && page > 0 ? page : 1,
  };
}

function withPage(sp: SP, page: number): string {
  const params = new URLSearchParams();
  for (const [k, v] of Object.entries(sp)) {
    if (k === "page" || v == null) continue;
    params.set(k, Array.isArray(v) ? v[0] : v);
  }
  params.set("page", String(page));
  return params.toString();
}
