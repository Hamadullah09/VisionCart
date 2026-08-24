import { test, describe } from "node:test";
import assert from "node:assert/strict";
import {
  solveTransform,
  measureFace,
  measurementAdvice,
  suggestSizeBand,
  estimateFaceShape,
  fitContain,
  fitCover,
  distance,
  midpoint,
  DEFAULT_ANCHORS,
  NO_ADJUSTMENT,
  IRIS_DIAMETER_MM,
  LM,
  type NormalizedLandmark,
} from "./geometry.ts";

/**
 * Tests for the virtual try-on geometry.
 *
 * These run against the *browser* implementation, not a C# port. The placement
 * maths executes only in the customer's browser, so a .NET reimplementation
 * would be tested code that never runs in production. Node 24 strips types
 * natively, so this needs no test framework and no build step.
 *
 * The file under test is byte-identical to the legacy `src/lib/tryon.ts`: the
 * migration preserves this maths exactly, and these tests exist to prove that.
 */

const ASSET_W = 600;
const ASSET_H = 200;

/** Builds a 478-point landmark array with the iris points placed deliberately. */
function face(opts: {
  leftIris: [number, number];
  rightIris: [number, number];
  irisRadius?: number;
  rightIrisRadius?: number;
  faceWidth?: number;
  faceHeight?: number;
  jawWidth?: number;
}): NormalizedLandmark[] {
  const r = opts.irisRadius ?? 0.01;
  const rr = opts.rightIrisRadius ?? r;
  const points: NormalizedLandmark[] = Array.from({ length: 478 }, () => ({ x: 0.5, y: 0.5 }));

  const [lx, ly] = opts.leftIris;
  const [rx, ry] = opts.rightIris;

  points[LM.leftIrisCenter] = { x: lx, y: ly };
  points[LM.leftIrisLeft] = { x: lx - r, y: ly };
  points[LM.leftIrisRight] = { x: lx + r, y: ly };
  points[LM.rightIrisCenter] = { x: rx, y: ry };
  points[LM.rightIrisLeft] = { x: rx - rr, y: ry };
  points[LM.rightIrisRight] = { x: rx + rr, y: ry };

  const fw = opts.faceWidth ?? 0.4;
  points[LM.leftFaceEdge] = { x: 0.5 - fw / 2, y: 0.5 };
  points[LM.rightFaceEdge] = { x: 0.5 + fw / 2, y: 0.5 };

  const fh = opts.faceHeight ?? 0.5;
  points[LM.foreheadTop] = { x: 0.5, y: 0.5 - fh / 2 };
  points[LM.chin] = { x: 0.5, y: 0.5 + fh / 2 };
  points[LM.noseTip] = { x: (lx + rx) / 2, y: (ly + ry) / 2 + 0.05 };

  const jw = opts.jawWidth ?? fw * 0.75;
  points[172] = { x: 0.5 - jw / 2, y: 0.62 };
  points[397] = { x: 0.5 + jw / 2, y: 0.62 };

  return points;
}

describe("solveTransform — the placement algorithm", () => {
  test("puts the anchors exactly on the pupils", () => {
    const leftPupil = { x: 300, y: 400 };
    const rightPupil = { x: 500, y: 400 };

    const t = solveTransform({
      leftPupil,
      rightPupil,
      assetWidth: ASSET_W,
      assetHeight: ASSET_H,
      anchors: DEFAULT_ANCHORS,
    });

    // Reproduce the canvas transform and confirm each anchor lands on its pupil.
    const apply = (px: number, py: number) => {
      const dx = px - t.anchorX;
      const dy = py - t.anchorY;
      const cos = Math.cos(t.rotate);
      const sin = Math.sin(t.rotate);
      return {
        x: t.translateX + t.scale * (dx * cos - dy * sin),
        y: t.translateY + t.scale * (dx * sin + dy * cos),
      };
    };

    const anchorL = apply(DEFAULT_ANCHORS.leftX * ASSET_W, DEFAULT_ANCHORS.leftY * ASSET_H);
    const anchorR = apply(DEFAULT_ANCHORS.rightX * ASSET_W, DEFAULT_ANCHORS.rightY * ASSET_H);

    assert.ok(distance(anchorL, leftPupil) < 1e-9, "left anchor must land on the left pupil");
    assert.ok(distance(anchorR, rightPupil) < 1e-9, "right anchor must land on the right pupil");
  });

  test("scale is the ratio of pupil span to anchor span", () => {
    const t = solveTransform({
      leftPupil: { x: 100, y: 100 },
      rightPupil: { x: 300, y: 100 },
      assetWidth: ASSET_W,
      assetHeight: ASSET_H,
      anchors: DEFAULT_ANCHORS,
    });

    const anchorSpan = (DEFAULT_ANCHORS.rightX - DEFAULT_ANCHORS.leftX) * ASSET_W; // 252
    assert.equal(t.scale, 200 / anchorSpan);
  });

  test("a tilted head rotates the frame by the same angle", () => {
    const t = solveTransform({
      leftPupil: { x: 100, y: 100 },
      rightPupil: { x: 200, y: 200 }, // 45 degrees
      assetWidth: ASSET_W,
      assetHeight: ASSET_H,
      anchors: DEFAULT_ANCHORS,
    });
    assert.ok(Math.abs(t.rotate - Math.PI / 4) < 1e-9);
  });

  test("a doubled pupil span doubles the scale — no other term changes", () => {
    const base = { assetWidth: ASSET_W, assetHeight: ASSET_H, anchors: DEFAULT_ANCHORS };
    const near = solveTransform({ ...base, leftPupil: { x: 0, y: 0 }, rightPupil: { x: 100, y: 0 } });
    const far = solveTransform({ ...base, leftPupil: { x: 0, y: 0 }, rightPupil: { x: 200, y: 0 } });

    assert.ok(Math.abs(far.scale - near.scale * 2) < 1e-12);
    assert.equal(far.rotate, near.rotate);
  });

  test("customer adjustments compose on top of the solved fit", () => {
    const base = {
      leftPupil: { x: 100, y: 100 },
      rightPupil: { x: 300, y: 100 },
      assetWidth: ASSET_W,
      assetHeight: ASSET_H,
      anchors: DEFAULT_ANCHORS,
    };

    const auto = solveTransform(base);
    const nudged = solveTransform({
      ...base,
      adjustment: { scale: 1.2, rotate: 0.1, offsetX: 7, offsetY: -3 },
    });

    assert.ok(Math.abs(nudged.scale - auto.scale * 1.2) < 1e-12);
    assert.ok(Math.abs(nudged.rotate - (auto.rotate + 0.1)) < 1e-12);
    assert.equal(nudged.translateX, auto.translateX + 7);
    assert.equal(nudged.translateY, auto.translateY - 3);
  });

  test("scaleAdj on the asset multiplies the fit", () => {
    const base = {
      leftPupil: { x: 0, y: 0 },
      rightPupil: { x: 200, y: 0 },
      assetWidth: ASSET_W,
      assetHeight: ASSET_H,
    };
    const plain = solveTransform({ ...base, anchors: DEFAULT_ANCHORS });
    const padded = solveTransform({ ...base, anchors: { ...DEFAULT_ANCHORS, scaleAdj: 1.5 } });
    assert.ok(Math.abs(padded.scale - plain.scale * 1.5) < 1e-12);
  });

  test("degenerate anchors do not produce a division by zero", () => {
    const t = solveTransform({
      leftPupil: { x: 0, y: 0 },
      rightPupil: { x: 100, y: 0 },
      assetWidth: ASSET_W,
      assetHeight: ASSET_H,
      anchors: { leftX: 0.5, leftY: 0.5, rightX: 0.5, rightY: 0.5, scaleAdj: 1 },
    });
    assert.ok(Number.isFinite(t.scale));
  });

  test("NO_ADJUSTMENT is the identity", () => {
    const base = {
      leftPupil: { x: 10, y: 20 },
      rightPupil: { x: 210, y: 20 },
      assetWidth: ASSET_W,
      assetHeight: ASSET_H,
      anchors: DEFAULT_ANCHORS,
    };
    assert.deepEqual(solveTransform(base), solveTransform({ ...base, adjustment: NO_ADJUSTMENT }));
  });
});

describe("measureFace — pupillary distance", () => {
  test("PD is pupil span scaled by the iris ruler", () => {
    // Iris 0.01 wide in normalised units => 10 px at width 1000.
    // Pupils 0.06 apart => 60 px. PD = 60 * 11.7 / 10 = 70.2 mm.
    const m = measureFace(
      face({ leftIris: [0.47, 0.5], rightIris: [0.53, 0.5], irisRadius: 0.005 }),
      1000,
      1000,
    );
    assert.ok(m);
    assert.equal(m!.pdMm, 70.2);
  });

  test("uses the documented 11.7 mm iris constant", () => {
    assert.equal(IRIS_DIAMETER_MM, 11.7);
  });

  test("a level head reports zero roll and high confidence", () => {
    const m = measureFace(
      face({ leftIris: [0.45, 0.5], rightIris: [0.55, 0.5], irisRadius: 0.008 }),
      1000,
      1000,
    )!;
    assert.equal(m.rollDeg, 0);
    assert.ok(m.confidence > 0.9, `expected high confidence, got ${m.confidence}`);
  });

  test("a turned head — mismatched iris widths — collapses confidence", () => {
    const straight = measureFace(
      face({ leftIris: [0.45, 0.5], rightIris: [0.55, 0.5], irisRadius: 0.008 }),
      1000, 1000,
    )!;
    const turned = measureFace(
      face({
        leftIris: [0.45, 0.5],
        rightIris: [0.55, 0.5],
        irisRadius: 0.008,
        rightIrisRadius: 0.004, // foreshortened
      }),
      1000, 1000,
    )!;

    assert.ok(turned.confidence < straight.confidence);
    assert.ok(turned.confidence < 0.3, `expected low confidence, got ${turned.confidence}`);
  });

  test("an implausible PD is capped at 0.2 confidence rather than reported as fact", () => {
    // Tiny pupil span against a normal iris gives a PD far below 48 mm.
    const m = measureFace(
      face({ leftIris: [0.499, 0.5], rightIris: [0.501, 0.5], irisRadius: 0.008 }),
      1000, 1000,
    )!;
    assert.ok(m.pdMm !== null && m.pdMm < 48, `expected an implausible PD, got ${m.pdMm}`);
    assert.ok(m.confidence <= 0.2, `expected confidence to be capped, got ${m.confidence}`);
  });

  test("head roll is reported in degrees", () => {
    const m = measureFace(
      face({ leftIris: [0.45, 0.45], rightIris: [0.55, 0.55], irisRadius: 0.008 }),
      1000, 1000,
    )!;
    assert.ok(Math.abs(m.rollDeg - 45) < 1e-6);
  });

  test("without iris points it still returns pupils but no PD", () => {
    const noIris: NormalizedLandmark[] = Array.from({ length: 468 }, () => ({ x: 0.5, y: 0.5 }));
    noIris[LM.leftEyeOuter] = { x: 0.4, y: 0.5 };
    noIris[LM.rightEyeOuter] = { x: 0.6, y: 0.5 };

    const m = measureFace(noIris, 1000, 1000)!;
    assert.ok(m, "must degrade rather than fail");
    assert.equal(m.pdMm, null, "no iris ruler means no measurement, not a guess");
    assert.equal(m.confidence, 0);
  });

  test("too few landmarks returns null rather than throwing", () => {
    assert.equal(measureFace([], 1000, 1000), null);
    assert.equal(measureFace(Array.from({ length: 100 }, () => ({ x: 0, y: 0 })), 100, 100), null);
  });

  test("PD is independent of image resolution", () => {
    const landmarks = face({ leftIris: [0.45, 0.5], rightIris: [0.55, 0.5], irisRadius: 0.008 });
    const small = measureFace(landmarks, 500, 500)!;
    const large = measureFace(landmarks, 4000, 4000)!;
    assert.ok(Math.abs(small.pdMm! - large.pdMm!) < 0.2);
  });
});

describe("guidance and sizing", () => {
  test("advice asks the customer to straighten a tilted head", () => {
    const tilted = measureFace(
      face({ leftIris: [0.45, 0.42], rightIris: [0.55, 0.58], irisRadius: 0.008 }),
      1000, 1000,
    )!;
    assert.match(measurementAdvice(tilted) ?? "", /straighten/i);
  });

  test("advice tells someone out of shot to move closer", () => {
    assert.match(measurementAdvice(null) ?? "", /closer/i);
  });

  test("a good capture produces no nagging", () => {
    const good = measureFace(
      face({ leftIris: [0.45, 0.5], rightIris: [0.55, 0.5], irisRadius: 0.008 }),
      1000, 1000,
    )!;
    assert.equal(measurementAdvice(good), null);
  });

  test("size bands follow the optical rule of thumb", () => {
    assert.equal(suggestSizeBand(120), "narrow");
    assert.equal(suggestSizeBand(135), "medium");
    assert.equal(suggestSizeBand(150), "wide");
    assert.equal(suggestSizeBand(null), null);
  });

  test("face shape classification returns a value from the constants list", () => {
    const allowed = ["oval", "round", "square", "heart", "diamond", "oblong"];
    const shape = estimateFaceShape(
      face({ leftIris: [0.45, 0.5], rightIris: [0.55, 0.5] }),
      1000, 1000,
    );
    assert.ok(shape && allowed.includes(shape), `unexpected shape ${shape}`);
  });

  test("a long narrow face reads as oblong", () => {
    const shape = estimateFaceShape(
      face({ leftIris: [0.45, 0.5], rightIris: [0.55, 0.5], faceWidth: 0.3, faceHeight: 0.6 }),
      1000, 1000,
    );
    assert.equal(shape, "oblong");
  });
});

describe("letterboxing — pupil coordinates must mean the same thing in both modes", () => {
  test("fitContain centres the whole image inside the box", () => {
    const r = fitContain(1000, 500, 800, 800);
    assert.equal(r.width, 800);
    assert.equal(r.height, 400);
    assert.equal(r.x, 0);
    assert.equal(r.y, 200);
  });

  test("fitCover fills the box and crops the overflow", () => {
    const r = fitCover(1000, 500, 800, 800);
    assert.equal(r.height, 800);
    assert.equal(r.width, 1600);
    assert.equal(r.x, -400);
  });

  test("both preserve aspect ratio", () => {
    for (const fit of [fitContain, fitCover]) {
      const r = fit(1600, 900, 640, 480);
      assert.ok(Math.abs(r.width / r.height - 1600 / 900) < 1e-9);
    }
  });
});

describe("primitives", () => {
  test("distance is euclidean", () => {
    assert.equal(distance({ x: 0, y: 0 }, { x: 3, y: 4 }), 5);
  });

  test("midpoint is the average", () => {
    assert.deepEqual(midpoint({ x: 0, y: 0 }, { x: 10, y: 20 }), { x: 5, y: 10 });
  });

  test("default anchors match the artwork generator's contract", () => {
    // These are one half of a two-part contract with
    // scripts/generate-frame-assets.mjs. Changing one without the other
    // misplaces every generated frame.
    assert.deepEqual(DEFAULT_ANCHORS, {
      leftX: 0.29, leftY: 0.5, rightX: 0.71, rightY: 0.5, scaleAdj: 1,
    });
  });
});
