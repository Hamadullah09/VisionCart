import type { FrameAnchors } from "@/lib/tryon";

/** Everything the try-on canvas needs about one frame colourway. */
export type TryOnFrame = {
  variantId: string;
  frameId: string;
  slug: string;
  name: string;
  brand: string | null;
  colorName: string;
  colorHex: string | null;
  /** Transparent PNG drawn over the face. Null = cannot be tried on. */
  overlayUrl: string | null;
  thumbUrl: string | null;
  priceMinor: number;
  compareAtMinor: number | null;
  anchors: FrameAnchors;
  opacity: number;
  shape: string | null;
  sizeBand: string | null;
  totalWidthMm: number | null;
};
