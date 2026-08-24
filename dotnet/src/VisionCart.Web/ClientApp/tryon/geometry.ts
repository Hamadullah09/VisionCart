/**
 * Virtual try-on geometry. Pure functions, no DOM and no server imports, so
 * the same maths runs in the canvas renderer, in tests and on the server when
 * baking a shareable snapshot.
 *
 * The whole approach rests on one idea: a frame overlay is calibrated by two
 * anchor points — where the wearer's pupils must sit inside the artwork. Given
 * the wearer's two pupils in the photo, there is exactly one similarity
 * transform (uniform scale + rotation + translation) that puts the anchors on
 * the pupils. No per-face tuning, no machine learning at render time.
 */

export type Point = { x: number; y: number };

export type FrameAnchors = {
  /** 0..1 of the overlay image, where the wearer's LEFT pupil should land. */
  leftX: number;
  leftY: number;
  rightX: number;
  rightY: number;
  /** Per-asset fudge factor for artwork with generous padding. */
  scaleAdj: number;
};

export const DEFAULT_ANCHORS: FrameAnchors = {
  leftX: 0.29,
  leftY: 0.5,
  rightX: 0.71,
  rightY: 0.5,
  scaleAdj: 1,
};

/** Live nudges the customer makes with the sliders. */
export type Adjustment = {
  /** Multiplies the solved scale. 1 = auto fit. */
  scale: number;
  /** Extra rotation in radians on top of the head tilt. */
  rotate: number;
  /** Offsets in canvas pixels; +y moves the frame down the nose. */
  offsetX: number;
  offsetY: number;
};

export const NO_ADJUSTMENT: Adjustment = { scale: 1, rotate: 0, offsetX: 0, offsetY: 0 };

export type Transform = {
  translateX: number;
  translateY: number;
  rotate: number;
  scale: number;
  /** Anchor point inside the overlay, in overlay pixels. */
  anchorX: number;
  anchorY: number;
};

export function distance(a: Point, b: Point): number {
  return Math.hypot(b.x - a.x, b.y - a.y);
}

export function midpoint(a: Point, b: Point): Point {
  return { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 };
}

/**
 * Solve the placement. `leftPupil`/`rightPupil` are in canvas pixels;
 * `assetWidth`/`assetHeight` are the natural size of the overlay image.
 */
export function solveTransform(args: {
  leftPupil: Point;
  rightPupil: Point;
  assetWidth: number;
  assetHeight: number;
  anchors: FrameAnchors;
  adjustment?: Adjustment;
}): Transform {
  const adj = args.adjustment ?? NO_ADJUSTMENT;
  const { anchors } = args;

  const anchorL: Point = { x: anchors.leftX * args.assetWidth, y: anchors.leftY * args.assetHeight };
  const anchorR: Point = { x: anchors.rightX * args.assetWidth, y: anchors.rightY * args.assetHeight };

  const anchorSpan = distance(anchorL, anchorR) || 1;
  const pupilSpan = distance(args.leftPupil, args.rightPupil);

  const scale = (pupilSpan / anchorSpan) * (anchors.scaleAdj || 1) * adj.scale;

  const anchorAngle = Math.atan2(anchorR.y - anchorL.y, anchorR.x - anchorL.x);
  const pupilAngle = Math.atan2(
    args.rightPupil.y - args.leftPupil.y,
    args.rightPupil.x - args.leftPupil.x,
  );
  const rotate = pupilAngle - anchorAngle + adj.rotate;

  return {
    translateX: args.leftPupil.x + adj.offsetX,
    translateY: args.leftPupil.y + adj.offsetY,
    rotate,
    scale,
    anchorX: anchorL.x,
    anchorY: anchorL.y,
  };
}

/**
 * Apply a solved transform and draw the overlay. Leaves the context exactly as
 * it found it, so callers can keep drawing afterwards.
 */
export function drawFrame(
  ctx: CanvasRenderingContext2D,
  image: CanvasImageSource,
  t: Transform,
  opts: { width: number; height: number; opacity?: number },
): void {
  ctx.save();
  ctx.globalAlpha = opts.opacity ?? 1;
  ctx.translate(t.translateX, t.translateY);
  ctx.rotate(t.rotate);
  ctx.scale(t.scale, t.scale);
  ctx.translate(-t.anchorX, -t.anchorY);
  ctx.drawImage(image, 0, 0, opts.width, opts.height);
  ctx.restore();
}

// --- MediaPipe landmark indices -------------------------------------------
// The 478-point face mesh appends five iris points per eye to the 468-point
// base mesh. Iris centres are the most stable pupil proxy the model gives.

export const LM = {
  leftIrisCenter: 468,
  leftIrisRight: 469,
  leftIrisTop: 470,
  leftIrisLeft: 471,
  leftIrisBottom: 472,
  rightIrisCenter: 473,
  rightIrisRight: 474,
  rightIrisTop: 475,
  rightIrisLeft: 476,
  rightIrisBottom: 477,
  // Outer eye corners, used when the model runs without iris refinement
  leftEyeOuter: 33,
  rightEyeOuter: 263,
  noseTip: 1,
  chin: 152,
  foreheadTop: 10,
  leftFaceEdge: 234,
  rightFaceEdge: 454,
} as const;

export type NormalizedLandmark = { x: number; y: number; z?: number };

/**
 * The horizontal visible iris of an adult eye is 11.7 mm and barely varies
 * between people — it is the reference every card-free PD measurement uses.
 */
export const IRIS_DIAMETER_MM = 11.7;

export type FaceMeasurement = {
  leftPupil: Point;
  rightPupil: Point;
  /** Estimated binocular pupillary distance, millimetres. */
  pdMm: number | null;
  /** Estimated distance from each pupil to the bridge centre. */
  pdLeftMm: number | null;
  pdRightMm: number | null;
  /** Head roll in degrees; large values mean "straighten up" advice. */
  rollDeg: number;
  /** 0..1 confidence in the PD figure. */
  confidence: number;
  /** Width of the face at the cheekbones, mm — drives frame size advice. */
  faceWidthMm: number | null;
};

/**
 * Turn normalised landmarks into pixel points plus a PD estimate.
 * `width`/`height` are the pixel size of the image the landmarks came from.
 */
export function measureFace(
  landmarks: NormalizedLandmark[],
  width: number,
  height: number,
): FaceMeasurement | null {
  if (!landmarks || landmarks.length < 468) return null;

  const px = (i: number): Point => ({ x: landmarks[i].x * width, y: landmarks[i].y * height });
  const hasIris = landmarks.length >= 478;

  const leftPupil = hasIris ? px(LM.leftIrisCenter) : px(LM.leftEyeOuter);
  const rightPupil = hasIris ? px(LM.rightIrisCenter) : px(LM.rightEyeOuter);

  const pupilSpanPx = distance(leftPupil, rightPupil);
  const rollDeg =
    (Math.atan2(rightPupil.y - leftPupil.y, rightPupil.x - leftPupil.x) * 180) / Math.PI;

  let pdMm: number | null = null;
  let pdLeftMm: number | null = null;
  let pdRightMm: number | null = null;
  let faceWidthMm: number | null = null;
  let confidence = 0;

  if (hasIris) {
    // Average both irises; a head turned slightly away foreshortens one of them.
    const leftIrisPx = distance(px(LM.leftIrisLeft), px(LM.leftIrisRight));
    const rightIrisPx = distance(px(LM.rightIrisLeft), px(LM.rightIrisRight));
    const irisPx = (leftIrisPx + rightIrisPx) / 2;

    if (irisPx > 2) {
      const mmPerPx = IRIS_DIAMETER_MM / irisPx;
      pdMm = round1(pupilSpanPx * mmPerPx);

      const bridge = px(LM.noseTip);
      pdRightMm = round1(distance(rightPupil, { x: bridge.x, y: rightPupil.y }) * mmPerPx);
      pdLeftMm = round1(distance(leftPupil, { x: bridge.x, y: leftPupil.y }) * mmPerPx);
      faceWidthMm = round1(distance(px(LM.leftFaceEdge), px(LM.rightFaceEdge)) * mmPerPx);

      // Irises of visibly different size mean the head is turned; the further
      // apart they are, the less the single-plane assumption holds.
      const asymmetry = Math.abs(leftIrisPx - rightIrisPx) / Math.max(leftIrisPx, rightIrisPx);
      const tiltPenalty = Math.min(1, Math.abs(rollDeg) / 25);
      confidence = clamp01(1 - asymmetry * 2.5 - tiltPenalty * 0.5);

      // A PD outside this window is anatomically implausible — better to admit
      // we don't know than to send a wrong number to the lab.
      if (pdMm < 48 || pdMm > 80) confidence = Math.min(confidence, 0.2);
    }
  }

  return { leftPupil, rightPupil, pdMm, pdLeftMm, pdRightMm, rollDeg, confidence, faceWidthMm };
}

/** Plain-language guidance shown under the try-on canvas. */
export function measurementAdvice(m: FaceMeasurement | null): string | null {
  if (!m) return "Move closer and make sure your whole face is in shot.";
  if (Math.abs(m.rollDeg) > 12) return "Straighten your head to level for an accurate fit.";
  if (m.confidence < 0.4) return "Look straight at the camera in even light for a better measurement.";
  return null;
}

/**
 * Frame width advice from the measured face width. Optical rule of thumb: the
 * frame should be about as wide as the face, within a few millimetres.
 */
export function suggestSizeBand(faceWidthMm: number | null): "narrow" | "medium" | "wide" | null {
  if (!faceWidthMm) return null;
  if (faceWidthMm < 128) return "narrow";
  if (faceWidthMm > 145) return "wide";
  return "medium";
}

/**
 * Classify face shape from the mesh, used to sort frames by what suits.
 * Deliberately coarse — it is a shopping aid, not a diagnosis.
 */
export function estimateFaceShape(
  landmarks: NormalizedLandmark[],
  width: number,
  height: number,
): string | null {
  if (!landmarks || landmarks.length < 468) return null;
  const px = (i: number): Point => ({ x: landmarks[i].x * width, y: landmarks[i].y * height });

  const faceWidth = distance(px(LM.leftFaceEdge), px(LM.rightFaceEdge));
  const faceHeight = distance(px(LM.foreheadTop), px(LM.chin));
  if (faceWidth < 1) return null;

  const ratio = faceHeight / faceWidth;
  // Jaw width relative to cheekbones separates square/round from heart/diamond.
  const jawWidth = distance(px(172), px(397));
  const jawRatio = jawWidth / faceWidth;

  if (ratio > 1.55) return "oblong";
  if (ratio < 1.25) return jawRatio > 0.78 ? "square" : "round";
  if (jawRatio < 0.68) return "heart";
  if (jawRatio > 0.85) return "square";
  return ratio > 1.4 ? "oval" : "diamond";
}

function round1(v: number): number {
  return Math.round(v * 10) / 10;
}

function clamp01(v: number): number {
  return Math.max(0, Math.min(1, v));
}

/**
 * Fit an image of `iw x ih` inside a `cw x ch` box, centred — returns the
 * draw rectangle. Used to letterbox uploads and webcam frames identically so
 * pupil coordinates mean the same thing in both modes.
 */
export function fitContain(iw: number, ih: number, cw: number, ch: number) {
  const scale = Math.min(cw / iw, ch / ih);
  const w = iw * scale;
  const h = ih * scale;
  return { x: (cw - w) / 2, y: (ch - h) / 2, width: w, height: h, scale };
}

/** Same as fitContain but fills the box, cropping the overflow (webcam). */
export function fitCover(iw: number, ih: number, cw: number, ch: number) {
  const scale = Math.max(cw / iw, ch / ih);
  const w = iw * scale;
  const h = ih * scale;
  return { x: (cw - w) / 2, y: (ch - h) / 2, width: w, height: h, scale };
}
