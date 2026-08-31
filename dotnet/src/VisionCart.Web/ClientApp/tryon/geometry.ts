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
/**
 * How dark the shadow a frame casts on the face is, at full strength.
 *
 * Real spectacles sit a few millimetres off the face and throw a soft shadow
 * onto the brow, the nose and the cheeks. Without one the overlay reads as a
 * sticker lying on the photograph rather than an object in front of it — it is
 * the single strongest depth cue available to a flat sprite.
 */
export const SHADOW_ALPHA = 0.28;

/** Shadow offset and blur, as fractions of the frame's drawn width. */
const SHADOW_OFFSET_X = 0.012;
const SHADOW_OFFSET_Y = 0.022;
const SHADOW_BLUR = 0.018;

/**
 * How far past the cheek an arm keeps drawing before it has faded out, as a
 * fraction of the pupil span.
 *
 * The temples run back towards the ears, so beyond the widest part of the face
 * they are behind the head and should not be visible at all. A flat sprite
 * draws them straight over the cheek, which is the other reason the overlay
 * reads as pasted on. Fading rather than cutting, because a hard edge is its
 * own artefact.
 */
export const TEMPLE_FADE = 0.22;

export type FaceSilhouette = {
  /** Canvas x of the widest point of the face on each side. */
  leftX: number;
  rightX: number;
};

/**
 * Where each temple fades out, in canvas pixels.
 *
 * Split from the drawing so the arithmetic can be tested: the compositing
 * itself needs a canvas, but getting the direction or the extent wrong would
 * erase the frame rather than the arms, and that is worth pinning.
 *
 * Returns, per side, the coordinate where the fade begins (fully opaque) and
 * where it has finished (fully erased). Everything past `end` is behind the
 * head entirely.
 */
export function templeFadeBounds(
  silhouette: FaceSilhouette,
): { left: { start: number; end: number }; right: { start: number; end: number } } {
  const span = Math.abs(silhouette.rightX - silhouette.leftX);
  const fade = span * TEMPLE_FADE;

  return {
    left: { start: silhouette.leftX, end: silhouette.leftX - fade },
    right: { start: silhouette.rightX, end: silhouette.rightX + fade },
  };
}

export function drawFrame(
  ctx: CanvasRenderingContext2D,
  image: CanvasImageSource,
  t: Transform,
  opts: {
    width: number;
    height: number;
    opacity?: number;
    /**
     * Horizontal foreshortening, 0..1. A frame is close to rigid and close to
     * flat, so a turned head sees its width fall away while its height barely
     * changes — which is why this squeezes one axis rather than scaling both.
     */
    squeezeX?: number;
    /** Draw the shadow the frame casts on the face. */
    shadow?: boolean;
    /** Fade the arms out where they pass behind the head. */
    silhouette?: FaceSilhouette | null;
    /** Scratch canvas for the occlusion pass; reused between frames. */
    scratch?: HTMLCanvasElement | null;
  },
): void {
  const opacity = opts.opacity ?? 1;
  const squeeze = opts.squeezeX ?? 1;
  const drawnWidth = opts.width * t.scale * squeeze;

  const place = (target: CanvasRenderingContext2D, dx = 0, dy = 0): void => {
    target.translate(t.translateX + dx, t.translateY + dy);
    target.rotate(t.rotate);
    target.scale(t.scale * squeeze, t.scale);
    target.translate(-t.anchorX, -t.anchorY);
    target.drawImage(image, 0, 0, opts.width, opts.height);
  };

  // --- the shadow it casts ------------------------------------------------
  // brightness(0) keeps the artwork's alpha and blackens everything inside it,
  // which turns any frame silhouette into its own shadow without a second asset.
  if (opts.shadow) {
    ctx.save();
    ctx.globalAlpha = SHADOW_ALPHA * opacity;
    ctx.filter = `blur(${(drawnWidth * SHADOW_BLUR).toFixed(2)}px) brightness(0)`;
    place(ctx, drawnWidth * SHADOW_OFFSET_X, drawnWidth * SHADOW_OFFSET_Y);
    ctx.restore();
  }

  // --- the frame itself ---------------------------------------------------
  // Without a silhouette to respect, draw straight to the canvas: an offscreen
  // pass every frame is a real cost on a camera feed and buys nothing here.
  if (!opts.silhouette || !opts.scratch) {
    ctx.save();
    ctx.globalAlpha = opacity;
    place(ctx);
    ctx.restore();
    return;
  }

  const scratch = opts.scratch;
  const sctx = scratch.getContext("2d");
  if (!sctx) {
    ctx.save();
    ctx.globalAlpha = opacity;
    place(ctx);
    ctx.restore();
    return;
  }

  // The scratch must share the caller's coordinate system exactly. The visible
  // context is running under a device-pixel-ratio transform, so a scratch left
  // at identity draws the frame at logical coordinates onto a backing-pixel
  // canvas — landing it in the wrong place at the wrong size by precisely that
  // factor. Copying the matrix is the whole fix.
  sctx.setTransform(1, 0, 0, 1, 0, 0);
  sctx.clearRect(0, 0, scratch.width, scratch.height);
  sctx.save();
  sctx.setTransform(ctx.getTransform());
  place(sctx);
  sctx.restore();

  // Erase the arms where the head is in front of them. destination-out on the
  // scratch canvas only, so the photograph underneath is untouched.
  // The silhouette is in logical units as well, so the mask is painted under
  // the same matrix rather than scaled by hand.
  const bounds = templeFadeBounds(opts.silhouette);
  sctx.setTransform(ctx.getTransform());
  sctx.globalCompositeOperation = "destination-out";

  const edge = Math.max(scratch.width, scratch.height);
  for (const [side, direction] of [
    [bounds.left, -1] as const,
    [bounds.right, 1] as const,
  ]) {
    const from = side.start;
    const to = side.end;
    const gradient = sctx.createLinearGradient(from, 0, to, 0);
    gradient.addColorStop(0, "rgba(0,0,0,0)");
    gradient.addColorStop(1, "rgba(0,0,0,1)");
    sctx.fillStyle = gradient;
    sctx.fillRect(Math.min(from, to), -edge, Math.abs(to - from), edge * 3);

    // Everything past the fade is fully behind the head.
    if (direction < 0) sctx.clearRect(-edge, -edge, edge + Math.min(from, to), edge * 3);
    else sctx.clearRect(Math.max(from, to), -edge, edge * 2, edge * 3);
  }
  sctx.globalCompositeOperation = "source-over";
  sctx.setTransform(1, 0, 0, 1, 0, 0);

  ctx.save();
  ctx.globalAlpha = opacity;
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.drawImage(scratch, 0, 0, ctx.canvas.width, ctx.canvas.height);
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

  /**
   * The widest points of the face, in the same pixel space as the pupils.
   *
   * Beyond these the temples are behind the head, so the renderer fades them
   * out — the difference between glasses that are worn and a sprite lying on
   * top of a photograph.
   */
  leftFaceEdge: Point;
  rightFaceEdge: Point;
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

  return {
    leftPupil, rightPupil, pdMm, pdLeftMm, pdRightMm, rollDeg, confidence, faceWidthMm,
    leftFaceEdge: px(LM.leftFaceEdge),
    rightFaceEdge: px(LM.rightFaceEdge),
  };
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

// --- automatic fit ----------------------------------------------------------

/** Physical dimensions of a frame, millimetres, as printed on the arm. */
export type FrameDimensions = {
  lensWidthMm?: number | null;
  bridgeWidthMm?: number | null;
  templeLengthMm?: number | null;
  totalWidthMm?: number | null;
};

export type AutoFit = {
  /** Ready to hand straight to solveTransform. */
  adjustment: Adjustment;

  /** Millimetres of face per canvas pixel, from the measured PD. */
  mmPerPixel: number | null;

  /** How wide the frame really is, and how wide this face is. */
  frameWidthMm: number | null;
  faceWidthMm: number | null;

  /**
   * frame width ÷ face width. Opticians look for roughly 1.0: a frame about as
   * wide as the face. Below ~0.92 it pinches, above ~1.08 it overhangs.
   */
  widthRatio: number | null;

  /** Head roll the fit had to correct for, degrees. */
  tiltDeg: number;

  /** Millimetres the frame was raised or lowered to sit on the bridge. */
  heightMm: number;

  /** Where the pupil sits in the lens vertically, as a fraction from the top. */
  pupilHeightInLens: number;

  verdict: "good" | "narrow" | "wide" | "unknown";
  notes: string[];
};

/**
 * Pupils should not sit in the dead centre of a lens.
 *
 * A lens is ground with its optical centre on the pupil, and the wearer looks
 * down far more than up — so the pupil belongs slightly above the middle,
 * leaving the greater part of the lens below it for reading and for the floor
 * in front of your feet. 0.45 is the usual single-vision compromise.
 */
export const PUPIL_HEIGHT_IN_LENS = 0.45;

/** Ratios of frame width to face width an optician would call a good fit. */
const RATIO_NARROW = 0.92;
const RATIO_WIDE = 1.08;

/**
 * Works out the size, height and tilt that suit this face, so the customer is
 * shown a real fit rather than a frame merely parked on their pupils.
 *
 * Three things are decided here, and each is a genuine optical rule rather than
 * a guess at what looks nice:
 *
 * **Size.** The base solve lands the frame's lens centres on the pupils, which
 * is correct optically but says nothing about whether the frame fits the head.
 * With a measured PD we know how many millimetres a pixel is worth, so the
 * frame can be drawn at its true manufactured width — a 145 mm frame on a
 * 130 mm face then visibly overhangs, because it would.
 *
 * **Height.** The frame is raised so the pupil sits at {@link PUPIL_HEIGHT_IN_LENS}
 * of the lens depth rather than halfway down it.
 *
 * **Tilt.** The head roll is already taken out by the base solve, so the extra
 * rotation is zero; the measured roll is reported instead, because past about
 * ten degrees the PD reading itself becomes unreliable and the customer is
 * better off straightening up than being silently corrected.
 */
export function autoFit(
  measurement: FaceMeasurement | null,
  frame: FrameDimensions | null,
  anchors: FrameAnchors = DEFAULT_ANCHORS,
): AutoFit {
  const notes: string[] = [];
  const base: AutoFit = {
    adjustment: { ...NO_ADJUSTMENT },
    mmPerPixel: null,
    frameWidthMm: null,
    faceWidthMm: measurement?.faceWidthMm ?? null,
    widthRatio: null,
    tiltDeg: measurement?.rollDeg ?? 0,
    heightMm: 0,
    pupilHeightInLens: PUPIL_HEIGHT_IN_LENS,
    verdict: "unknown",
    notes,
  };

  if (!measurement) {
    notes.push("No face was found, so the frame is placed by hand.");
    return base;
  }

  // --- size ---------------------------------------------------------------
  const pupilSpanPx = distance(measurement.leftPupil, measurement.rightPupil);

  if (measurement.pdMm && pupilSpanPx > 0) {
    base.mmPerPixel = measurement.pdMm / pupilSpanPx;
  }

  const frameWidthMm = frame?.totalWidthMm
    ?? (frame?.lensWidthMm && frame?.bridgeWidthMm
      ? frame.lensWidthMm * 2 + frame.bridgeWidthMm
      : null);
  base.frameWidthMm = frameWidthMm;

  if (frameWidthMm && base.mmPerPixel) {
    // Width the base solve would draw: the anchor span scales to the pupil
    // span, and the whole asset scales with it.
    const anchorSpan = Math.abs(anchors.rightX - anchors.leftX);
    const solvedWidthPx = anchorSpan > 0 ? pupilSpanPx / anchorSpan : 0;
    const trueWidthPx = frameWidthMm / base.mmPerPixel;

    if (solvedWidthPx > 0) {
      // Clamped: a wrong PD reading must not blow the frame up to fill the
      // screen or shrink it to a dot.
      base.adjustment.scale = clamp(trueWidthPx / solvedWidthPx, 0.7, 1.4);
    }
  }

  // --- fit verdict --------------------------------------------------------
  if (frameWidthMm && measurement.faceWidthMm) {
    const ratio = frameWidthMm / measurement.faceWidthMm;
    base.widthRatio = ratio;

    if (ratio < RATIO_NARROW) {
      base.verdict = "narrow";
      notes.push(`This frame is ${Math.round((1 - ratio) * 100)}% narrower than the face — it will pinch at the temples.`);
    } else if (ratio > RATIO_WIDE) {
      base.verdict = "wide";
      notes.push(`This frame is ${Math.round((ratio - 1) * 100)}% wider than the face — it will slide down.`);
    } else {
      base.verdict = "good";
      notes.push("The frame width suits this face.");
    }
  }

  // --- height -------------------------------------------------------------
  // Anchors put the pupil at anchors.leftY down the asset. Moving it to
  // PUPIL_HEIGHT_IN_LENS means shifting the frame down the nose by the
  // difference, measured against the asset's own height.
  const anchorY = (anchors.leftY + anchors.rightY) / 2;

  // The anchor is the point of the artwork that lands on the pupil. To have the
  // pupil sit HIGHER in the lens the artwork must move DOWN, so the shift is
  // the anchor minus the target, not the other way round.
  const shiftFraction = anchorY - PUPIL_HEIGHT_IN_LENS;

  if (Math.abs(shiftFraction) > 0.001 && frameWidthMm && base.mmPerPixel) {
    // Assets are drawn about 2.6 times as wide as they are tall.
    const assetHeightPx = (frameWidthMm / base.mmPerPixel) / 2.6;
    const offsetPx = shiftFraction * assetHeightPx;
    base.adjustment.offsetY = offsetPx;
    base.heightMm = offsetPx * base.mmPerPixel;
  }

  // --- tilt ---------------------------------------------------------------
  base.adjustment.rotate = 0;

  if (Math.abs(measurement.rollDeg) > 10) {
    notes.push(`Your head is tilted ${Math.abs(Math.round(measurement.rollDeg))}° — straighten up for a truer measurement.`);
  }

  if (measurement.confidence < 0.5) {
    notes.push("The measurement is uncertain. Better light and a straight-on photo will improve it.");
  }

  return base;
}

function clamp(value: number, low: number, high: number): number {
  return Math.min(high, Math.max(low, value));
}
