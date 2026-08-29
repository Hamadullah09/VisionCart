import { describe, it } from "node:test";
import assert from "node:assert/strict";

import {
  estimatePose,
  yawForeshortening,
  yawBridgeShift,
  NEUTRAL_POSE,
  EXTREME_YAW_DEG,
  NOSE_REST_FRACTION,
  MIN_YAW_FORESHORTENING,
} from "./pose.ts";
import { LM, type NormalizedLandmark } from "./geometry.ts";

import {
  OneEuroFilter,
  PoseSmoother,
  holdThroughLoss,
  HOLD_MS,
  FADE_MS,
} from "./smoothing.ts";

/**
 * Builds a mesh with only the landmarks the pose estimator reads.
 *
 * Normalised coordinates, as MediaPipe emits them: 0..1 across the image, y
 * increasing downward.
 */
function mesh(over: Partial<Record<keyof typeof LM, [number, number]>> = {}): NormalizedLandmark[] {
  const points: Partial<Record<number, [number, number]>> = {
    [LM.leftIrisCenter]: [0.42, 0.44],
    [LM.rightIrisCenter]: [0.58, 0.44],
    [LM.leftEyeOuter]: [0.38, 0.44],
    [LM.rightEyeOuter]: [0.62, 0.44],
    // eyes 0.44, forehead 0.235, chin 0.76 → span 0.525.
    // A level head puts the nose NOSE_REST_FRACTION of that below the eyes.
    [LM.noseTip]: [0.50, 0.44 + 0.525 * NOSE_REST_FRACTION],
    [LM.chin]: [0.50, 0.76],
    [LM.foreheadTop]: [0.50, 0.235],
    [LM.leftFaceEdge]: [0.33, 0.50],
    [LM.rightFaceEdge]: [0.67, 0.50],
  };

  for (const [name, xy] of Object.entries(over)) {
    points[LM[name as keyof typeof LM]] = xy as [number, number];
  }

  const out: NormalizedLandmark[] = [];
  for (let i = 0; i < 478; i++) {
    const p = points[i];
    out.push(p ? { x: p[0], y: p[1] } : { x: 0.5, y: 0.5 });
  }
  return out;
}

describe("head pose", () => {
  it("reads a face looking straight at the camera as neutral", () => {
    const pose = estimatePose(mesh());

    assert.ok(Math.abs(pose.rollDeg) < 0.5, `roll ${pose.rollDeg}`);
    assert.ok(Math.abs(pose.yawDeg) < 1, `yaw ${pose.yawDeg}`);
    assert.ok(Math.abs(pose.pitchDeg) < 3, `pitch ${pose.pitchDeg}`);
    assert.ok(pose.confidence > 0.8);
    assert.equal(pose.isExtreme, false);
  });

  it("reports a lean as roll", () => {
    // Right eye lower than the left: head tipped toward their right.
    const pose = estimatePose(mesh({ leftIrisCenter: [0.42, 0.42], rightIrisCenter: [0.58, 0.46] }));
    assert.ok(pose.rollDeg > 10, `expected a clear roll, got ${pose.rollDeg}`);
  });

  it("reports a turn as yaw, with a sign that follows the direction", () => {
    // Turned toward their right: the right half of the face foreshortens, so
    // the nose sits closer to that edge.
    const right = estimatePose(mesh({ noseTip: [0.57, 0.44 + 0.525 * NOSE_REST_FRACTION] }));
    const left = estimatePose(mesh({ noseTip: [0.43, 0.44 + 0.525 * NOSE_REST_FRACTION] }));

    assert.ok(right.yawDeg < -8, `turned right should be negative, got ${right.yawDeg}`);
    assert.ok(left.yawDeg > 8, `turned left should be positive, got ${left.yawDeg}`);
    assert.ok(Math.abs(right.yawDeg + left.yawDeg) < 1, "should be symmetric");
  });

  it("reports a nod as pitch, with a sign that follows the direction", () => {
    // Chin down drives the nose further below the eye line.
    const down = estimatePose(mesh({ noseTip: [0.50, 0.63] }));
    const up = estimatePose(mesh({ noseTip: [0.50, 0.51] }));

    assert.ok(down.pitchDeg < -5, `chin down should be negative, got ${down.pitchDeg}`);
    assert.ok(up.pitchDeg > 5, `chin up should be positive, got ${up.pitchDeg}`);
  });

  it("trusts a turned face less than a straight one", () => {
    const straight = estimatePose(mesh());
    const turned = estimatePose(mesh({ noseTip: [0.60, 0.44 + 0.525 * NOSE_REST_FRACTION] }));

    // Every measurement is taken across a face that is foreshortening.
    assert.ok(turned.confidence < straight.confidence);
  });

  it("trusts a face too small in frame much less", () => {
    const far = estimatePose(mesh({
      leftIrisCenter: [0.487, 0.44], rightIrisCenter: [0.513, 0.44],
      leftEyeOuter: [0.484, 0.44], rightEyeOuter: [0.516, 0.44],
    }));
    assert.ok(far.confidence < 0.6, `expected low confidence, got ${far.confidence}`);
  });

  it("flags the angle past which a flat overlay stops convincing", () => {
    const fine = estimatePose(mesh({ noseTip: [0.53, 0.44 + 0.525 * NOSE_REST_FRACTION] }));
    const steep = estimatePose(mesh({ noseTip: [0.66, 0.44 + 0.525 * NOSE_REST_FRACTION] }));

    assert.equal(fine.isExtreme, false);
    assert.equal(steep.isExtreme, true);
    assert.ok(Math.abs(steep.yawDeg) > EXTREME_YAW_DEG);
  });

  it("returns neutral rather than guessing when the mesh is absent", () => {
    assert.deepEqual(estimatePose(null), NEUTRAL_POSE);
    assert.deepEqual(estimatePose([]), NEUTRAL_POSE);
    assert.deepEqual(estimatePose(mesh().slice(0, 100)), NEUTRAL_POSE);
  });
});

describe("yaw foreshortening", () => {
  it("leaves a face-on frame at full width", () => {
    assert.equal(yawForeshortening(0), 1);
  });

  it("narrows the frame as the head turns", () => {
    assert.ok(yawForeshortening(20) < 1);
    assert.ok(yawForeshortening(30) < yawForeshortening(20));
  });

  it("is symmetric — turning either way looks the same head-on", () => {
    assert.ok(Math.abs(yawForeshortening(25) - yawForeshortening(-25)) < 1e-9);
  });

  it("never squeezes the frame to nothing", () => {
    // A frame seen edge-on is a different drawing; crushing this one is worse
    // than leaving it slightly wide.
    assert.equal(yawForeshortening(90), MIN_YAW_FORESHORTENING);
    assert.ok(yawForeshortening(75) >= MIN_YAW_FORESHORTENING);
  });

  it("slides the bridge across the face as the head turns", () => {
    assert.equal(yawBridgeShift(0), 0);
    assert.ok(yawBridgeShift(25) > 0);
    assert.ok(yawBridgeShift(-25) < 0);
  });
});

describe("one euro filter", () => {
  it("passes the very first sample through untouched", () => {
    const f = new OneEuroFilter();
    assert.equal(f.filter(10, 0), 10);
  });

  it("damps noise on a signal that is not moving", () => {
    const f = new OneEuroFilter();
    const noise = [100, 103, 97, 102, 98, 101, 99];

    let last = 0;
    noise.forEach((v, i) => { last = f.filter(v, i / 30); });

    // Raw samples swing ±3; the filter should sit far closer to the truth.
    assert.ok(Math.abs(last - 100) < 2, `expected steadiness, got ${last}`);
  });

  it("keeps up with a signal that is genuinely moving", () => {
    const steady = new OneEuroFilter();
    const moving = new OneEuroFilter();

    // A ramp: the filter must not lag far behind it.
    let out = 0;
    for (let i = 0; i < 30; i++) out = moving.filter(i * 10, i / 30);
    for (let i = 0; i < 30; i++) steady.filter(0, i / 30);

    // Adaptive cutoff: a fixed average would trail much further than this.
    assert.ok(out > 250, `expected it to keep up, reached ${out} of 290`);
  });

  it("survives a repeated or backwards timestamp", () => {
    // A stalled rAF or a clock adjustment must not divide by zero.
    const f = new OneEuroFilter();
    f.filter(5, 1);
    assert.equal(f.filter(9, 1), 5);
    assert.equal(f.filter(9, 0.5), 5);
    assert.ok(Number.isFinite(f.filter(9, 2)));
  });

  it("forgets its history on reset", () => {
    const f = new OneEuroFilter();
    f.filter(100, 0);
    f.filter(100, 0.1);
    assert.ok(f.isPrimed);

    f.reset();
    assert.equal(f.isPrimed, false);
    assert.equal(f.filter(7, 0.2), 7);
  });
});

describe("pose smoother", () => {
  it("smooths every quantity that places a frame", () => {
    const s = new PoseSmoother();
    const still = { leftX: 100, leftY: 200, rightX: 220, rightY: 200, yawDeg: 0, pitchDeg: 0 };

    s.smooth(still, 0);
    const jittered = s.smooth(
      { ...still, leftX: 106, rightX: 214, yawDeg: 7 }, 1 / 30);

    assert.ok(jittered.leftX < 106, "x should be pulled back toward the truth");
    assert.ok(jittered.rightX > 214);
    assert.ok(jittered.yawDeg < 7, "angles should be damped harder than positions");
  });

  it("starts clean for a new subject", () => {
    const s = new PoseSmoother();
    s.smooth({ leftX: 0, leftY: 0, rightX: 0, rightY: 0, yawDeg: 0, pitchDeg: 0 }, 0);
    s.reset();

    // A new photo must not blend out of the previous one's pose.
    const first = s.smooth(
      { leftX: 500, leftY: 300, rightX: 620, rightY: 300, yawDeg: 12, pitchDeg: -4 }, 1);
    assert.equal(first.leftX, 500);
    assert.equal(first.yawDeg, 12);
  });
});

describe("holding through a lost face", () => {
  it("keeps drawing through a blink", () => {
    const held = holdThroughLoss(HOLD_MS / 2);
    assert.equal(held.usePrevious, true);
    assert.equal(held.opacity, 1);
  });

  it("fades rather than cutting once the hold expires", () => {
    const fading = holdThroughLoss(HOLD_MS + FADE_MS / 2);
    assert.equal(fading.usePrevious, true);
    assert.ok(fading.opacity > 0 && fading.opacity < 1);
  });

  it("gives up once the face is properly gone", () => {
    const gone = holdThroughLoss(HOLD_MS + FADE_MS + 1);
    assert.equal(gone.usePrevious, false);
    assert.equal(gone.opacity, 0);
  });

  it("never strobes — opacity falls monotonically", () => {
    let previous = 1.01;
    for (let ms = 0; ms <= HOLD_MS + FADE_MS + 50; ms += 20) {
      const { opacity } = holdThroughLoss(ms);
      assert.ok(opacity <= previous + 1e-9, `opacity rose at ${ms}ms`);
      previous = opacity;
    }
  });
});
