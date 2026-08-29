/**
 * Head pose from a 2D face mesh.
 *
 * The mirror previously knew one thing about a head: the angle of the line
 * between the pupils. That is enough to rotate a frame when somebody leans,
 * and nothing at all when they turn or nod — the frame stayed dead-on while
 * the face beneath it moved away, which is the single largest reason a flat
 * overlay stops looking like glasses.
 *
 * These are **estimates from a flat projection**, not a solved 3D pose. A full
 * perspective-n-point solve needs a camera intrinsic matrix the browser does
 * not give us, and inventing one would make the numbers look authoritative
 * without making them true. What follows is deliberately simple, deliberately
 * bounded, and reports how much it trusts itself — so the renderer can back
 * off gracefully instead of pretending.
 */

import type { NormalizedLandmark, Point } from "./geometry.ts";
import { LM, distance, midpoint } from "./geometry.ts";

export type HeadPose = {
  /** Lean, degrees. Positive = the wearer's head tipped toward their right. */
  rollDeg: number;

  /** Turn, degrees. Positive = turned toward their right; 0 = facing the lens. */
  yawDeg: number;

  /** Nod, degrees. Positive = chin up; 0 = level. */
  pitchDeg: number;

  /**
   * 0..1. Falls away as the head turns, because every quantity here is
   * measured across a face that is foreshortening.
   */
  confidence: number;

  /** True once the angle is steep enough that a flat overlay stops convincing. */
  isExtreme: boolean;
};

export const NEUTRAL_POSE: HeadPose = {
  rollDeg: 0, yawDeg: 0, pitchDeg: 0, confidence: 0, isExtreme: false,
};

/**
 * Past this the far lens would need to be genuinely occluded by the cheek, and
 * a flat sprite cannot do that. Chosen by eye against the validation set rather
 * than derived: beyond roughly this angle the illusion breaks regardless of
 * what the transform does.
 */
export const EXTREME_YAW_DEG = 28;
export const EXTREME_PITCH_DEG = 22;

/**
 * Maps the raw landmark ratios onto degrees.
 *
 * Neither ratio is an angle. Both are monotonic in the angle over the range
 * that matters, so a linear map is honest enough for deciding how to draw a
 * frame — and it is calibrated against the range a face actually reaches
 * before the overlay stops working anyway.
 */
const YAW_RATIO_TO_DEG = 74;
const PITCH_RATIO_TO_DEG = 96;

/**
 * Where the nose tip sits below the eye line on a level head, as a fraction of
 * the forehead-to-chin span. Pitch is the departure from this.
 */
export const NOSE_REST_FRACTION = 0.245;

/**
 * Pupil separation, as a fraction of image width, below which the face is too
 * small in frame for these ratios to mean anything. At 8% of a 1280-pixel
 * image the pupils are about 100px apart; much under that and a landmark's
 * own error is a large share of every distance measured from it.
 */
export const MIN_EYE_SPAN = 0.08;

/** How much each condition costs the confidence score. */
const YAW_TRUST_COST = 0.55;
const PITCH_TRUST_COST = 0.3;
const SIZE_TRUST_COST = 0.8;

export function estimatePose(landmarks: NormalizedLandmark[] | null): HeadPose {
  if (!landmarks || landmarks.length < 468) return NEUTRAL_POSE;

  const at = (i: number): Point | null => {
    const l = landmarks[i];
    return l ? { x: l.x, y: l.y } : null;
  };

  const leftEye = at(LM.leftIrisCenter) ?? at(LM.leftEyeOuter);
  const rightEye = at(LM.rightIrisCenter) ?? at(LM.rightEyeOuter);
  const nose = at(LM.noseTip);
  const chin = at(LM.chin);
  const forehead = at(LM.foreheadTop);
  const leftEdge = at(LM.leftFaceEdge);
  const rightEdge = at(LM.rightFaceEdge);

  if (!leftEye || !rightEye || !nose || !chin || !forehead || !leftEdge || !rightEdge) {
    return NEUTRAL_POSE;
  }

  // --- roll ---------------------------------------------------------------
  // The eye line, straight from the mesh. Unlike the other two this one is a
  // real angle in the image plane and needs no calibration.
  const rollDeg = Math.atan2(rightEye.y - leftEye.y, rightEye.x - leftEye.x) * (180 / Math.PI);

  // --- yaw ----------------------------------------------------------------
  // Turning the head foreshortens the cheek that swings away. The nose stays
  // put between them, so the imbalance of the two half-widths tracks the turn.
  const toLeft = Math.abs(nose.x - leftEdge.x);
  const toRight = Math.abs(rightEdge.x - nose.x);
  const spanX = toLeft + toRight;
  const yawRatio = spanX > 1e-6 ? (toRight - toLeft) / spanX : 0;
  const yawDeg = clamp(yawRatio * YAW_RATIO_TO_DEG, -90, 90);

  // --- pitch --------------------------------------------------------------
  // Nodding slides the nose along the forehead-to-chin span. Measured as a
  // fraction of that span, so it survives the face being near or far.
  const eyeLine = midpoint(leftEye, rightEye);
  const faceHeight = Math.abs(chin.y - forehead.y);
  const noseBelowEyes = nose.y - eyeLine.y;

  const pitchRatio = faceHeight > 1e-6
    ? (noseBelowEyes / faceHeight) - NOSE_REST_FRACTION
    : 0;
  const pitchDeg = clamp(-pitchRatio * PITCH_RATIO_TO_DEG, -60, 60);

  // --- confidence ---------------------------------------------------------
  // Everything above is measured across a face that foreshortens as it turns,
  // so trust has to fall with the angle rather than be asserted once.
  const yawPenalty = Math.min(1, Math.abs(yawDeg) / 45);
  const pitchPenalty = Math.min(1, Math.abs(pitchDeg) / 35);
  const eyeSpan = distance(leftEye, rightEye);
  const sizePenalty = eyeSpan < MIN_EYE_SPAN ? 1 - eyeSpan / MIN_EYE_SPAN : 0;

  const confidence = clamp(
    1 - YAW_TRUST_COST * yawPenalty
      - PITCH_TRUST_COST * pitchPenalty
      - SIZE_TRUST_COST * sizePenalty,
    0, 1);

  return {
    rollDeg,
    yawDeg,
    pitchDeg,
    confidence,
    isExtreme: Math.abs(yawDeg) > EXTREME_YAW_DEG || Math.abs(pitchDeg) > EXTREME_PITCH_DEG,
  };
}

/**
 * How much narrower a frame looks at this yaw.
 *
 * A frame is close to rigid and close to flat, so as the head turns its
 * width falls away with the cosine of the angle while its height barely
 * changes. Applying that to the horizontal axis alone is what stops a turned
 * head from wearing a frame that is visibly too wide for it.
 *
 * Floored, because a frame seen edge-on is a different drawing altogether and
 * squeezing this one to nothing looks worse than leaving it slightly wide.
 */
export const MIN_YAW_FORESHORTENING = 0.72;

export function yawForeshortening(yawDeg: number): number {
  const cos = Math.cos((yawDeg * Math.PI) / 180);
  return Math.max(MIN_YAW_FORESHORTENING, cos);
}

/**
 * How far the frame slides across the face at this yaw, as a fraction of the
 * pupil span.
 *
 * The bridge rests on the nose, and the nose is in front of the eyes. Turn the
 * head and the nose swings across the face relative to the pupils, taking the
 * frame with it. Without this the frame stays centred on the pupil midpoint
 * and visibly detaches from the nose.
 */
export const YAW_BRIDGE_SHIFT = 0.16;

export function yawBridgeShift(yawDeg: number): number {
  return Math.sin((yawDeg * Math.PI) / 180) * YAW_BRIDGE_SHIFT;
}

function clamp(v: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, v));
}
