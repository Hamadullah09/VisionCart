/**
 * What a downloaded try-on picture is called.
 *
 * A try-on image is only meaningful alongside the PD it was drawn at. The whole
 * renderer hangs on that one number — it is what converts pupil separation in
 * pixels into millimetres, and therefore what decides how big the frame comes
 * out. Two pictures of the same face wearing the same frame at different PDs
 * are different pictures, and once they are sitting in a downloads folder there
 * is nothing else left to tell them apart.
 *
 * So the PD travels in the filename, and its provenance travels with it: a
 * figure the customer typed is written plainly, while one the detector guessed
 * is marked `estimated`. `workingPd` refuses to blend the two for the same
 * reason this refuses to flatten them — an estimate that arrives at the lab
 * looking like a measurement is how a remake happens.
 *
 * No DOM and no canvas here, so the naming rules are testable on their own.
 */

/** The frame, and the PD it was drawn at, for a picture about to be saved. */
export interface SnapshotNaming {
  /** Slug of the frame on the face, e.g. `ravi`. */
  slug?: string | null;
  /** The PD the render actually used, in millimetres. */
  pdMm?: number | null;
  /** Where that PD came from. An estimate is labelled as one. */
  pdSource?: "entered" | "estimated" | null;
}

/** Used when no frame is selected, which the download button should prevent. */
const FALLBACK_SLUG = "frame";

/**
 * The name a downloaded snapshot is saved under.
 *
 * `tryon-ravi-pd-58.jpg` for a PD the customer gave us, and
 * `tryon-ravi-pd-61.5-estimated.jpg` for one the camera worked out. A picture
 * taken before any PD is known keeps the plain `tryon-ravi.jpg` — better no
 * number than a wrong one.
 *
 * The measurements that go with a picture are written by the same rule with a
 * `txt` extension, so the pair sort together in a downloads folder and stay
 * obviously the same fitting.
 */
export function snapshotFilename(naming: SnapshotNaming, extension = "jpg"): string {
  const slug = cleanSlug(naming.slug);
  const pd = formatPd(naming.pdMm);

  if (pd === null) return `tryon-${slug}.${extension}`;

  const provenance = naming.pdSource === "estimated" ? "-estimated" : "";
  return `tryon-${slug}-pd-${pd}${provenance}.${extension}`;
}

/**
 * The PD as it appears in a filename.
 *
 * Rounded to a tenth, matching what the measurements panel displays, so the
 * file and the screen never disagree about the number. A whole millimetre
 * loses its `.0` — `58`, not `58.0`.
 */
function formatPd(pdMm: number | null | undefined): string | null {
  if (pdMm === null || pdMm === undefined) return null;
  if (!Number.isFinite(pdMm) || pdMm <= 0) return null;

  const rounded = Math.round(pdMm * 10) / 10;
  if (rounded <= 0) return null;

  return String(rounded);
}

/**
 * Reduce a slug to what is safe in a filename on every platform we ship to.
 *
 * Slugs arrive from our own catalogue and are already tame, but a filename
 * handed to the browser is not the place to find that out.
 */
function cleanSlug(slug: string | null | undefined): string {
  if (!slug) return FALLBACK_SLUG;

  const cleaned = slug
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");

  return cleaned || FALLBACK_SLUG;
}
