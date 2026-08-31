/**
 * Physical fitting: turning a real frame and a real face into a transform.
 *
 * The previous engine solved one thing — put the artwork's two anchor points on
 * the wearer's two pupils. That always "works", and it is why it was wrong: a
 * 130 mm frame and a 145 mm frame both landed on the pupils, so both were drawn
 * at the same size on the same face. The customer could not tell them apart,
 * because the geometry had thrown away the only fact that distinguishes them.
 *
 * This module inverts that. The frame's size is fixed — it is a manufactured
 * object with millimetres printed on the arm. What varies is the **scale of the
 * photograph**, and the pupillary distance is what reveals it:
 *
 *     pixels per millimetre = pupil separation in pixels / PD in millimetres
 *
 * Everything else follows. The frame occupies `totalWidthMm × pixelsPerMm`
 * pixels; the lens aperture is `lensHeightMm × pixelsPerMm` deep; the lens
 * optical centres sit `lensWidthMm + bridgeWidthMm` apart, which is a fixed
 * property of the frame and generally *not* equal to the wearer's PD. That last
 * difference is decentration — a real quantity a dispensing optician measures
 * and a lab grinds out — and it is reported rather than silently corrected.
 *
 * Two people with PD 62 and PD 68, photographed at the same pupil separation in
 * pixels, are therefore at different distances from the lens, so the same frame
 * is drawn at different pixel sizes on each. That is the point.
 *
 * No DOM, no network, no frame-specific branches. Everything here is a function
 * of the database row and the landmarks.
 */

import type { Adjustment, FrameAnchors, Point, Transform } from "./geometry.ts";
import { distance, midpoint } from "./geometry.ts";
import { yawBridgeShift, yawForeshortening } from "./pose.ts";

// --- what the database gives us --------------------------------------------

/**
 * The measurements printed on a real arm, in millimetres.
 *
 * The three-number code on an arm — 52□18-140 — is lens width, bridge width and
 * arm length. `totalWidthMm` is the fourth, less commonly printed figure: the
 * full width of the frame front, hinge to hinge. It is **not** the sum of the
 * other two plus a lens, because the end pieces either side add several
 * millimetres each; on this catalogue that difference runs 12–18 mm, which is
 * exactly why the two cannot be used interchangeably.
 */
export type FramePhysical = {
  lensWidthMm?: number | null;
  bridgeWidthMm?: number | null;
  lensHeightMm?: number | null;
  totalWidthMm?: number | null;
  templeLengthMm?: number | null;
};

/**
 * Where the frame is inside its own artwork — all fractions of the image, 0..1.
 *
 * A PNG of a frame is mostly padding and arms. To draw the frame at a physical
 * width we have to know which part of the picture *is* the frame front, and to
 * seat it on a face we have to know where its lens centres and lens aperture
 * are. Six numbers, all measurable from the artwork, none of them tuned per
 * product.
 */
export type FrameCalibration = {
  /** Optical centre of each lens. x ordered left-to-right in image space. */
  leftLensCenterX: number;
  leftLensCenterY: number;
  rightLensCenterX: number;
  rightLensCenterY: number;

  /** Horizontal extent of the frame FRONT — the span `totalWidthMm` measures. */
  frontLeftX: number;
  frontRightX: number;

  /** Vertical extent of the lens aperture — the span `lensHeightMm` measures. */
  lensTopY: number;
  lensBottomY: number;
};

/**
 * The artwork this project generates: a 1000×420 canvas with lens centres at
 * x = 290 and 710, y = 210.
 *
 * Every other value here is derived from that same generator, so the defaults
 * describe the shipped assets exactly rather than approximating them. Uploaded
 * product photography is calibrated in the back office instead; these apply
 * only where nothing has been measured.
 */
export const DEFAULT_CALIBRATION: FrameCalibration = {
  leftLensCenterX: 0.29,
  leftLensCenterY: 0.5,
  rightLensCenterX: 0.71,
  rightLensCenterY: 0.5,
  frontLeftX: 0.142,
  frontRightX: 0.858,
  lensTopY: 0.271,
  lensBottomY: 0.729,
};

/** Build a calibration from the four legacy anchor columns plus new bounds. */
export function calibrationFrom(
  anchors: Pick<FrameAnchors, "leftX" | "leftY" | "rightX" | "rightY">,
  bounds?: Partial<Pick<FrameCalibration, "frontLeftX" | "frontRightX" | "lensTopY" | "lensBottomY">> | null,
): FrameCalibration {
  return {
    leftLensCenterX: anchors.leftX,
    leftLensCenterY: anchors.leftY,
    rightLensCenterX: anchors.rightX,
    rightLensCenterY: anchors.rightY,
    frontLeftX: bounds?.frontLeftX ?? DEFAULT_CALIBRATION.frontLeftX,
    frontRightX: bounds?.frontRightX ?? DEFAULT_CALIBRATION.frontRightX,
    lensTopY: bounds?.lensTopY ?? DEFAULT_CALIBRATION.lensTopY,
    lensBottomY: bounds?.lensBottomY ?? DEFAULT_CALIBRATION.lensBottomY,
  };
}

// --- pupillary distance -----------------------------------------------------

/**
 * The range a binocular PD can plausibly fall in.
 *
 * Adults sit near 54–74 mm and children lower. Outside this window the number
 * is a typing slip, not a face — and a wrong PD scales the whole try-on, so it
 * is worth catching before it silently ruins every frame the customer views.
 *
 * These bound *plausibility*, not correctness: a PD inside them can still be
 * wrong, which is why the fitting copy never calls a value verified.
 */
export const PD_MIN_MM = 40;
export const PD_MAX_MM = 85;

/** Narrower band; outside it we ask the customer to double-check, not refuse. */
export const PD_TYPICAL_MIN_MM = 52;
export const PD_TYPICAL_MAX_MM = 78;

export type PdCheck = {
  ok: boolean;
  /** True when it is inside the wider range but outside the usual one. */
  unusual: boolean;
  message: string | null;
};

export function checkPd(pdMm: number | null | undefined): PdCheck {
  if (pdMm === null || pdMm === undefined || !Number.isFinite(pdMm)) {
    return { ok: false, unusual: false, message: null };
  }
  if (pdMm < PD_MIN_MM || pdMm > PD_MAX_MM) {
    return {
      ok: false,
      unusual: true,
      message: `A PD of ${round1(pdMm)} mm isn't a distance a pair of eyes can be apart. Most people are between ${PD_TYPICAL_MIN_MM} and ${PD_TYPICAL_MAX_MM} mm.`,
    };
  }
  if (pdMm < PD_TYPICAL_MIN_MM || pdMm > PD_TYPICAL_MAX_MM) {
    return {
      ok: true,
      unusual: true,
      message: `Please check your PD — ${round1(pdMm)} mm is outside the usual range. It's right for some people, so we'll use it either way.`,
    };
  }
  return { ok: true, unusual: false, message: null };
}

/**
 * How far apart the entered and measured PD can be before we say so.
 *
 * A camera estimate keyed to the iris is good to a couple of millimetres in
 * decent light and much worse in poor light, so the threshold has to sit above
 * its own noise floor or the warning fires constantly.
 */
export const PD_AGREEMENT_MM = 4;

export type PdAgreement = "agrees" | "differs" | "unknown";

export function comparePd(enteredMm: number | null, estimatedMm: number | null, estimateConfidence: number): {
  agreement: PdAgreement;
  message: string | null;
} {
  if (enteredMm === null || estimatedMm === null || estimateConfidence < 0.45) {
    return { agreement: "unknown", message: null };
  }
  if (Math.abs(enteredMm - estimatedMm) <= PD_AGREEMENT_MM) {
    return { agreement: "agrees", message: "Your PD matches what we measure from the photo." };
  }
  return {
    agreement: "differs",
    message:
      "We're having trouble matching your PD to the photo. Check that you're facing the camera straight on in even light — we'll keep using the PD you entered.",
  };
}

// --- the fit ----------------------------------------------------------------

/**
 * Where the pupil belongs in the lens, measured down from the top of the
 * aperture.
 *
 * A single-vision lens is ground with its optical centre on the pupil, and
 * people look down far more than they look up — so the pupil sits a little
 * above the middle, leaving the larger part of the lens below it for reading
 * and for the pavement. 0.45 is the usual dispensing compromise.
 */
export const PUPIL_HEIGHT_IN_LENS = 0.45;

/** Frame-width-to-face-width ratios a dispenser would call a good fit. */
export const RATIO_NARROW = 0.92;
export const RATIO_WIDE = 1.08;

/** Decentration beyond this per eye is worth telling the customer about. */
export const DECENTRATION_NOTE_MM = 4;

export type FaceInput = {
  /** Pupil centres in canvas pixels. Order does not matter. */
  leftPupil: Point;
  rightPupil: Point;
  /** Widest point of the face each side, canvas pixels — for the size verdict. */
  leftFaceEdge?: Point | null;
  rightFaceEdge?: Point | null;
  /** Head angles in degrees, already smoothed. */
  rollDeg: number;
  yawDeg?: number;
  pitchDeg?: number;
};

export type FitVerdict = "good" | "narrow" | "wide" | "unknown";

export type FrameFit = {
  /** Ready for `drawFrame`. */
  transform: Transform;

  /** Horizontal squeeze for the turned head, 0..1 — passed to `drawFrame`. */
  squeezeX: number;

  /** The scale of the photograph: canvas pixels per millimetre of face. */
  pixelsPerMm: number;

  /** Which PD produced that scale, and its value. */
  pdMm: number;
  pdSource: "entered" | "estimated";

  /** Physical facts about the frame, restated for the fit panel. */
  frameWidthMm: number | null;
  lensWidthMm: number | null;
  lensHeightMm: number | null;
  bridgeWidthMm: number | null;

  /** Distance between the frame's two lens optical centres, millimetres. */
  frameCentreDistanceMm: number | null;

  /**
   * Millimetres each lens centre sits away from the pupil it serves. Positive
   * means the frame is wider between centres than the wearer's eyes are.
   */
  decentrationMm: number | null;

  /** Face width at the cheekbones in millimetres, on the same PD-derived scale. */
  faceWidthMm: number | null;

  /** frame width ÷ face width. Dispensers look for roughly 1.0. */
  widthRatio: number | null;
  verdict: FitVerdict;

  /** Head angles the fit accounted for. */
  rollDeg: number;
  yawDeg: number;

  /** Plain-language observations for the fit panel. */
  notes: string[];
};

/**
 * Solve the placement of one frame on one face.
 *
 * `artworkWidth`/`artworkHeight` are the natural pixel size of the overlay
 * image; `manual` is the customer's own nudge, applied last so that automatic
 * and manual placement never fight over the same number.
 */
export function solveFit(args: {
  face: FaceInput;
  pdMm: number;
  pdSource: "entered" | "estimated";
  physical: FramePhysical;
  calibration: FrameCalibration;
  artworkWidth: number;
  artworkHeight: number;
  manual?: Adjustment | null;
}): FrameFit | null {
  const { face, calibration: cal, physical } = args;
  const notes: string[] = [];

  if (!(args.pdMm > 0) || !(args.artworkWidth > 0) || !(args.artworkHeight > 0)) return null;

  // Order the pupils by image x so "left" means the same thing here as it does
  // in the calibration, whichever way round the landmarks arrived.
  const [pupilL, pupilR] = face.leftPupil.x <= face.rightPupil.x
    ? [face.leftPupil, face.rightPupil]
    : [face.rightPupil, face.leftPupil];

  const projectedSpanPx = distance(pupilL, pupilR);
  if (projectedSpanPx <= 0) return null;

  const yawDeg = face.yawDeg ?? 0;

  // --- the scale of the photograph ----------------------------------------
  // A turned head foreshortens the pupil separation, so the raw span understates
  // the frontal one. Dividing it back out is what keeps the frame from shrinking
  // every time the wearer looks away.
  const foreshortening = yawForeshortening(yawDeg);
  const frontalSpanPx = projectedSpanPx / foreshortening;
  const pixelsPerMm = frontalSpanPx / args.pdMm;

  // --- how big the frame is, in this photograph ---------------------------
  const lensWidthMm = positive(physical.lensWidthMm);
  const bridgeWidthMm = positive(physical.bridgeWidthMm);
  const lensHeightMm = positive(physical.lensHeightMm);

  // Prefer the measured front width. Falling back to lens+bridge+lens ignores
  // the end pieces and understates the frame by 12–18 mm, so it is a last
  // resort and the fit panel says which was used.
  const measuredWidth = positive(physical.totalWidthMm);
  const frameWidthMm = measuredWidth
    ?? (lensWidthMm && bridgeWidthMm ? lensWidthMm * 2 + bridgeWidthMm : null);

  if (!measuredWidth && frameWidthMm) {
    notes.push("This frame's overall width hasn't been recorded, so it's estimated from the lens and bridge — the fit may read a few millimetres narrow.");
  }

  const frontSpanFraction = cal.frontRightX - cal.frontLeftX;

  // --- the uniform scale applied to the artwork ---------------------------
  // One number for both axes. Scaling x and y independently would stretch the
  // frame out of shape, which no amount of correct sizing would excuse.
  let scale: number;

  if (frameWidthMm && frontSpanFraction > 0) {
    const frontWidthPx = frameWidthMm * pixelsPerMm;
    scale = frontWidthPx / (frontSpanFraction * args.artworkWidth);
  } else {
    // Nothing physical to go on: fall back to landing the lens centres on the
    // pupils, which is the old behaviour and is at least never absurd.
    const centreSpanPx = Math.abs(cal.rightLensCenterX - cal.leftLensCenterX) * args.artworkWidth;
    scale = centreSpanPx > 0 ? frontalSpanPx / centreSpanPx : 1;
    notes.push("This frame has no measurements on file, so it's placed on your pupils rather than drawn to size.");
  }

  // --- where it sits ------------------------------------------------------
  const eyeMid = midpoint(pupilL, pupilR);
  const rollRad = Math.atan2(pupilR.y - pupilL.y, pupilR.x - pupilL.x);

  // Down the nose: the pupil belongs above the middle of the lens, so the lens
  // centre sits below the pupil by the difference. Measured against the lens
  // aperture in millimetres, which is why lensHeightMm matters here and not
  // merely as a spec-sheet number.
  const lensDepthPx = lensHeightMm
    ? lensHeightMm * pixelsPerMm
    : (cal.lensBottomY - cal.lensTopY) * args.artworkHeight * scale;

  const seatDropPx = (0.5 - PUPIL_HEIGHT_IN_LENS) * lensDepthPx;

  // Across the face: the bridge rests on the nose, and the nose swings across
  // the face as the head turns, taking the frame with it.
  const bridgeShiftPx = yawBridgeShift(yawDeg) * projectedSpanPx;

  // The seat is expressed along the tilted eye line so a leaning head does not
  // slide the frame sideways off the nose.
  const cos = Math.cos(rollRad);
  const sin = Math.sin(rollRad);

  const manual = args.manual;
  const translateX = eyeMid.x + bridgeShiftPx * cos - seatDropPx * sin + (manual?.offsetX ?? 0);
  const translateY = eyeMid.y + bridgeShiftPx * sin + seatDropPx * cos + (manual?.offsetY ?? 0);

  // The anchor is the midpoint of the artwork's two lens centres: the point on
  // the frame that sits over the bridge of the nose.
  const anchorX = ((cal.leftLensCenterX + cal.rightLensCenterX) / 2) * args.artworkWidth;
  const anchorY = ((cal.leftLensCenterY + cal.rightLensCenterY) / 2) * args.artworkHeight;

  const transform: Transform = {
    translateX,
    translateY,
    rotate: rollRad + (manual?.rotate ?? 0),
    scale: scale * (manual?.scale ?? 1),
    anchorX,
    anchorY,
  };

  // --- what to tell the customer ------------------------------------------
  const frameCentreDistanceMm = lensWidthMm && bridgeWidthMm ? lensWidthMm + bridgeWidthMm : null;

  // Half the difference, because it is shared between the two eyes: this is the
  // figure a dispenser writes on the order.
  const decentrationMm = frameCentreDistanceMm !== null
    ? round1((frameCentreDistanceMm - args.pdMm) / 2)
    : null;

  let faceWidthMm: number | null = null;
  if (face.leftFaceEdge && face.rightFaceEdge) {
    const faceWidthPx = distance(face.leftFaceEdge, face.rightFaceEdge) / foreshortening;
    faceWidthMm = round1(faceWidthPx / pixelsPerMm);
  }

  let widthRatio: number | null = null;
  let verdict: FitVerdict = "unknown";

  if (frameWidthMm && faceWidthMm) {
    widthRatio = frameWidthMm / faceWidthMm;
    if (widthRatio < RATIO_NARROW) {
      verdict = "narrow";
      notes.push(`This frame is ${Math.round((1 - widthRatio) * 100)}% narrower than your face — it will press on your temples.`);
    } else if (widthRatio > RATIO_WIDE) {
      verdict = "wide";
      notes.push(`This frame is ${Math.round((widthRatio - 1) * 100)}% wider than your face — it will slide down your nose.`);
    } else {
      verdict = "good";
      notes.push("This frame is a good width for your face.");
    }
  }

  if (decentrationMm !== null && Math.abs(decentrationMm) >= DECENTRATION_NOTE_MM) {
    notes.push(
      decentrationMm > 0
        ? `Your eyes sit ${Math.abs(decentrationMm)} mm inside this frame's lens centres. Your lenses will be ground to match, which makes them a little thicker at the edge.`
        : `Your eyes sit ${Math.abs(decentrationMm)} mm outside this frame's lens centres. Your lenses will be ground to match.`,
    );
  }

  if (Math.abs(face.rollDeg) > 10) {
    notes.push(`Your head is tilted ${Math.abs(Math.round(face.rollDeg))}° — straighten up for a truer fit.`);
  }

  return {
    transform,
    squeezeX: foreshortening,
    pixelsPerMm,
    pdMm: args.pdMm,
    pdSource: args.pdSource,
    frameWidthMm: frameWidthMm ? round1(frameWidthMm) : null,
    lensWidthMm,
    lensHeightMm,
    bridgeWidthMm,
    frameCentreDistanceMm,
    decentrationMm,
    faceWidthMm,
    widthRatio,
    verdict,
    rollDeg: face.rollDeg,
    yawDeg,
    notes,
  };
}

// --- data quality -----------------------------------------------------------

/**
 * Whether a frame's artwork and its measurements describe the same object.
 *
 * The artwork gives three independent readings of the same scale: the span
 * between lens centres against `lensWidth + bridge`, the frame front against
 * `totalWidth`, and the lens aperture against `lensHeight`. On a correctly
 * drawn asset all three agree. When they disagree the try-on is drawing
 * something that is not the product, and the person who can fix that is an
 * administrator — so this exists to tell them, rather than to quietly render an
 * inaccurate frame.
 */
export const CALIBRATION_TOLERANCE = 0.06;

export type FrameDataIssue = {
  field: string;
  message: string;
  severity: "blocking" | "warning";
};

export type FrameDataCheck = {
  /** False when the frame cannot be drawn to size at all. */
  canFitPhysically: boolean;
  issues: FrameDataIssue[];
  /** The three px-per-mm readings, for the admin calibration screen. */
  scales: { fromLensCentres: number | null; fromFront: number | null; fromLensHeight: number | null };
  /** Largest disagreement between them, as a fraction. Null if fewer than two. */
  spread: number | null;
};

export function checkFrameData(
  physical: FramePhysical,
  calibration: FrameCalibration | null,
  artworkWidth: number | null,
  artworkHeight: number | null,
  hasArtwork: boolean,
): FrameDataCheck {
  const issues: FrameDataIssue[] = [];
  const lens = positive(physical.lensWidthMm);
  const bridge = positive(physical.bridgeWidthMm);
  const lensHeight = positive(physical.lensHeightMm);
  const total = positive(physical.totalWidthMm);

  if (!hasArtwork) {
    issues.push({ field: "TryOnImageUrl", severity: "blocking", message: "No try-on artwork has been uploaded for this colourway." });
  }
  if (!lens) issues.push({ field: "LensWidthMm", severity: "blocking", message: "Lens width is missing." });
  if (!bridge) issues.push({ field: "BridgeWidthMm", severity: "blocking", message: "Bridge width is missing." });
  if (!total) issues.push({ field: "TotalWidthMm", severity: "warning", message: "Overall frame width is missing, so the frame is sized from lens and bridge and will read a few millimetres narrow." });
  if (!lensHeight) issues.push({ field: "LensHeightMm", severity: "warning", message: "Lens height is missing, so the frame cannot be seated at the correct height on the nose." });

  // Physically impossible combinations, which a typo produces easily.
  if (lens && total && total < lens * 2 + (bridge ?? 0)) {
    issues.push({
      field: "TotalWidthMm",
      severity: "blocking",
      message: `Overall width (${total} mm) is less than two lenses plus the bridge (${lens * 2 + (bridge ?? 0)} mm). One of the three is wrong.`,
    });
  }
  // A lens is never much deeper than it is wide, and never a fraction of it.
  // Both directions catch the same mistake — a figure entered in centimetres,
  // or a width and a height swapped over.
  if (lens && lensHeight && (lensHeight > lens * 1.4 || lensHeight < lens * 0.4)) {
    issues.push({
      field: "LensHeightMm",
      severity: "warning",
      message: `A lens height of ${lensHeight} mm doesn't go with a ${lens} mm lens width. Check the units.`,
    });
  }
  if (lens && (lens < 30 || lens > 70)) {
    issues.push({ field: "LensWidthMm", severity: "warning", message: `A ${lens} mm lens is outside the range frames are normally made in (30–70 mm).` });
  }
  if (bridge && (bridge < 10 || bridge > 30)) {
    issues.push({ field: "BridgeWidthMm", severity: "warning", message: `A ${bridge} mm bridge is outside the usual range (10–30 mm).` });
  }

  const scales = { fromLensCentres: null as number | null, fromFront: null as number | null, fromLensHeight: null as number | null };

  if (calibration && artworkWidth && artworkHeight) {
    if (lens && bridge) {
      const px = Math.abs(calibration.rightLensCenterX - calibration.leftLensCenterX) * artworkWidth;
      if (px > 0) scales.fromLensCentres = px / (lens + bridge);
    }
    if (total) {
      const px = Math.abs(calibration.frontRightX - calibration.frontLeftX) * artworkWidth;
      if (px > 0) scales.fromFront = px / total;
    }
    if (lensHeight) {
      const px = Math.abs(calibration.lensBottomY - calibration.lensTopY) * artworkHeight;
      if (px > 0) scales.fromLensHeight = px / lensHeight;
    }
  }

  const readings = [scales.fromLensCentres, scales.fromFront, scales.fromLensHeight].filter(
    (v): v is number => v !== null && v > 0,
  );

  let spread: number | null = null;
  if (readings.length >= 2) {
    const lo = Math.min(...readings);
    const hi = Math.max(...readings);
    spread = (hi - lo) / hi;

    if (spread > CALIBRATION_TOLERANCE) {
      issues.push({
        field: "calibration",
        severity: "warning",
        message: `The artwork and the measurements disagree by ${Math.round(spread * 100)}%. The frame will be drawn at its recorded width, but the lenses and bridge in the picture won't line up with the numbers. Re-run the calibration for this colourway.`,
      });
    }
  }

  return {
    canFitPhysically: !issues.some((i) => i.severity === "blocking"),
    issues,
    scales,
    spread,
  };
}

function positive(v: number | null | undefined): number | null {
  return typeof v === "number" && Number.isFinite(v) && v > 0 ? v : null;
}

function round1(v: number): number {
  return Math.round(v * 10) / 10;
}
