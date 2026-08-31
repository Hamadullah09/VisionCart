import { describe, it } from "node:test";
import assert from "node:assert/strict";

import {
  solveFit,
  checkFrameData,
  checkPd,
  comparePd,
  calibrationFrom,
  DEFAULT_CALIBRATION,
  PUPIL_HEIGHT_IN_LENS,
  PD_TYPICAL_MIN_MM,
  PD_TYPICAL_MAX_MM,
  CALIBRATION_TOLERANCE,
  type FaceInput,
  type FrameCalibration,
  type FramePhysical,
} from "./fit.ts";

/**
 * A calibration whose frame front fills the artwork exactly.
 *
 * That makes the arithmetic legible: the drawn artwork width IS the frame's
 * physical width, so a test can assert on millimetres without unpicking the
 * padding an asset happens to carry.
 */
const SNUG: FrameCalibration = {
  leftLensCenterX: 0.25,
  leftLensCenterY: 0.5,
  rightLensCenterX: 0.75,
  rightLensCenterY: 0.5,
  frontLeftX: 0,
  frontRightX: 1,
  lensTopY: 0.2,
  lensBottomY: 0.8,
};

/** Ravi, from the seeded catalogue: 52□18-140, 37 mm deep, 138 mm across. */
const RAVI: FramePhysical = {
  lensWidthMm: 52,
  bridgeWidthMm: 18,
  lensHeightMm: 37,
  totalWidthMm: 138,
  templeLengthMm: 140,
};

const ART_W = 1000;
const ART_H = 420;

/** A level face with pupils `spanPx` apart, centred at (cx, cy). */
function face(spanPx: number, over: Partial<FaceInput> = {}, cx = 500, cy = 300): FaceInput {
  return {
    leftPupil: { x: cx - spanPx / 2, y: cy },
    rightPupil: { x: cx + spanPx / 2, y: cy },
    leftFaceEdge: { x: cx - spanPx, y: cy + 40 },
    rightFaceEdge: { x: cx + spanPx, y: cy + 40 },
    rollDeg: 0,
    yawDeg: 0,
    pitchDeg: 0,
    ...over,
  };
}

function fit(args: {
  spanPx?: number;
  pdMm?: number;
  physical?: FramePhysical;
  calibration?: FrameCalibration;
  faceOver?: Partial<FaceInput>;
  artW?: number;
  artH?: number;
} = {}) {
  const solved = solveFit({
    face: face(args.spanPx ?? 310, args.faceOver),
    pdMm: args.pdMm ?? 62,
    pdSource: "entered",
    physical: args.physical ?? RAVI,
    calibration: args.calibration ?? SNUG,
    artworkWidth: args.artW ?? ART_W,
    artworkHeight: args.artH ?? ART_H,
  });
  assert.ok(solved, "expected a fit");
  return solved;
}

/** The width the frame is actually painted at, in canvas pixels. */
function drawnWidthPx(f: ReturnType<typeof fit>, calibration = SNUG, artW = ART_W): number {
  return (calibration.frontRightX - calibration.frontLeftX) * artW * f.transform.scale;
}

describe("the scale of the photograph", () => {
  it("reads pixels per millimetre straight off the PD", () => {
    // The worked example: pupils 310 px apart on a 62 mm PD is 5 px/mm.
    assert.equal(fit({ spanPx: 310, pdMm: 62 }).pixelsPerMm, 5);
  });

  it("maps a 100 mm frame onto 500 px at that scale", () => {
    const f = fit({ spanPx: 310, pdMm: 62, physical: { ...RAVI, totalWidthMm: 100 } });
    assert.equal(Math.round(drawnWidthPx(f)), 500);
  });

  it("draws each frame at its own manufactured width", () => {
    // The whole point: two frames on one face must not come out the same size.
    const narrow = fit({ physical: { ...RAVI, totalWidthMm: 130 } });
    const wide = fit({ physical: { ...RAVI, totalWidthMm: 145 } });

    assert.equal(Math.round(drawnWidthPx(narrow)), Math.round(130 * 5));
    assert.equal(Math.round(drawnWidthPx(wide)), Math.round(145 * 5));
    assert.ok(drawnWidthPx(wide) > drawnWidthPx(narrow));
  });

  it("shrinks the same frame as the stated PD grows", () => {
    // Same photograph, same pupils. A larger PD means the face is further away,
    // so every millimetre is worth fewer pixels and the frame draws smaller.
    const widths = [58, 60, 62, 64, 66, 68].map((pd) => drawnWidthPx(fit({ pdMm: pd })));

    for (let i = 1; i < widths.length; i++) {
      assert.ok(widths[i] < widths[i - 1], `PD grew but the frame did not shrink at index ${i}`);
    }
    // And the physical width is unchanged throughout — only the scale moved.
    assert.equal(fit({ pdMm: 58 }).frameWidthMm, fit({ pdMm: 68 }).frameWidthMm);
  });

  it("grows the frame as the face comes closer", () => {
    const near = drawnWidthPx(fit({ spanPx: 400 }));
    const far = drawnWidthPx(fit({ spanPx: 200 }));
    assert.ok(near > far);
    // Twice the pupil span is twice the frame: the mapping is linear.
    assert.ok(Math.abs(drawnWidthPx(fit({ spanPx: 400 })) / drawnWidthPx(fit({ spanPx: 200 })) - 2) < 1e-9);
  });

  it("is independent of the resolution the landmarks came from", () => {
    // Doubling the image doubles the pupil span and the drawn frame with it, so
    // the frame covers the same fraction of the face either way.
    const small = fit({ spanPx: 155 });
    const large = fit({ spanPx: 310 });
    assert.ok(Math.abs(drawnWidthPx(large) / drawnWidthPx(small) - 2) < 1e-9);
  });

  it("refuses a face with no separation between the pupils rather than dividing by zero", () => {
    const collapsed = solveFit({
      face: face(0),
      pdMm: 62,
      pdSource: "entered",
      physical: RAVI,
      calibration: SNUG,
      artworkWidth: ART_W,
      artworkHeight: ART_H,
    });
    assert.equal(collapsed, null);
  });

  it("refuses a PD of zero", () => {
    assert.equal(
      solveFit({
        face: face(310), pdMm: 0, pdSource: "entered", physical: RAVI,
        calibration: SNUG, artworkWidth: ART_W, artworkHeight: ART_H,
      }),
      null,
    );
  });
});

describe("artwork padding", () => {
  it("scales up an asset whose frame occupies only part of the picture", () => {
    // The shipped artwork draws the arms too, so the frame front is ~72% of it.
    const padded = fit({ calibration: DEFAULT_CALIBRATION });
    const snug = fit({ calibration: SNUG });

    // Both must paint the same 138 mm of frame; the padded one needs a bigger
    // scale to do it, because more of its picture is not frame.
    assert.ok(padded.transform.scale > snug.transform.scale);
    assert.equal(
      Math.round(drawnWidthPx(padded, DEFAULT_CALIBRATION)),
      Math.round(drawnWidthPx(snug)),
    );
  });
});

describe("seating the frame on the face", () => {
  it("puts the pupil above the middle of the lens, not in the centre of it", () => {
    const f = fit();
    // 37 mm lens at 5 px/mm is 185 px deep; the pupil belongs 45% down it, so
    // the lens centre sits 5% of that below the pupil.
    const expected = 300 + (0.5 - PUPIL_HEIGHT_IN_LENS) * 37 * 5;
    assert.ok(Math.abs(f.transform.translateY - expected) < 1e-9, `${f.transform.translateY} vs ${expected}`);
    assert.ok(f.transform.translateY > 300, "the frame must sit below the pupil line, not on it");
  });

  it("seats a deeper lens lower, because there is more lens below the pupil", () => {
    const shallow = fit({ physical: { ...RAVI, lensHeightMm: 30 } });
    const deep = fit({ physical: { ...RAVI, lensHeightMm: 45 } });
    assert.ok(deep.transform.translateY > shallow.transform.translateY);
  });

  it("centres the frame between the pupils on a level face", () => {
    assert.equal(fit().transform.translateX, 500);
  });

  it("anchors on the midpoint of the artwork's two lens centres", () => {
    const f = fit();
    assert.equal(f.transform.anchorX, 0.5 * ART_W);
    assert.equal(f.transform.anchorY, 0.5 * ART_H);
  });

  it("still seats a frame whose lens height was never recorded", () => {
    const f = fit({ physical: { ...RAVI, lensHeightMm: null } });
    assert.ok(Number.isFinite(f.transform.translateY));
    assert.ok(f.transform.translateY > 300, "it should still drop below the pupil line");
  });
});

describe("head roll", () => {
  const cases = [0, 5, -5, 10, -10, 18, -18];

  it("rotates the frame to the eye line", () => {
    for (const deg of cases) {
      const rad = (deg * Math.PI) / 180;
      const span = 310;
      const f = solveFit({
        face: {
          leftPupil: { x: 500 - (span / 2) * Math.cos(rad), y: 300 - (span / 2) * Math.sin(rad) },
          rightPupil: { x: 500 + (span / 2) * Math.cos(rad), y: 300 + (span / 2) * Math.sin(rad) },
          leftFaceEdge: { x: 190, y: 340 }, rightFaceEdge: { x: 810, y: 340 },
          rollDeg: deg, yawDeg: 0,
        },
        pdMm: 62, pdSource: "entered", physical: RAVI, calibration: SNUG,
        artworkWidth: ART_W, artworkHeight: ART_H,
      });
      assert.ok(f, `no fit at ${deg}°`);
      assert.ok(
        Math.abs(f.transform.rotate - rad) < 1e-9,
        `at ${deg}° the frame rotated ${(f.transform.rotate * 180) / Math.PI}°`,
      );
    }
  });

  it("does not change the frame's size when the head leans", () => {
    // A leaning head is no further away, so the frame must not resize.
    const level = fit();
    const rad = (12 * Math.PI) / 180;
    const leaning = solveFit({
      face: {
        leftPupil: { x: 500 - 155 * Math.cos(rad), y: 300 - 155 * Math.sin(rad) },
        rightPupil: { x: 500 + 155 * Math.cos(rad), y: 300 + 155 * Math.sin(rad) },
        rollDeg: 12, yawDeg: 0,
      },
      pdMm: 62, pdSource: "entered", physical: RAVI, calibration: SNUG,
      artworkWidth: ART_W, artworkHeight: ART_H,
    });
    assert.ok(leaning);
    assert.ok(Math.abs(leaning.transform.scale - level.transform.scale) < 1e-9);
  });

  it("keeps the seat down the nose rather than down the screen", () => {
    // Tilt the head and the frame must slide along the tilted face, so the
    // horizontal placement moves too. A frame that only ever moved in screen-y
    // would detach from the nose the moment anybody leaned.
    const rad = (20 * Math.PI) / 180;
    const leaning = solveFit({
      face: {
        leftPupil: { x: 500 - 155 * Math.cos(rad), y: 300 - 155 * Math.sin(rad) },
        rightPupil: { x: 500 + 155 * Math.cos(rad), y: 300 + 155 * Math.sin(rad) },
        rollDeg: 20, yawDeg: 0,
      },
      pdMm: 62, pdSource: "entered", physical: RAVI, calibration: SNUG,
      artworkWidth: ART_W, artworkHeight: ART_H,
    });
    assert.ok(leaning);
    assert.ok(Math.abs(leaning.transform.translateX - 500) > 1, "the seat should follow the tilt");
  });

  it("advises straightening up past ten degrees, and stays quiet below it", () => {
    assert.ok(fit({ faceOver: { rollDeg: 15 } }).notes.some((n) => n.includes("tilted")));
    assert.ok(!fit({ faceOver: { rollDeg: 4 } }).notes.some((n) => n.includes("tilted")));
  });
});

describe("head yaw", () => {
  it("narrows the frame without shrinking it", () => {
    const straight = fit();
    const turned = fit({ faceOver: { yawDeg: 25 } });

    assert.ok(turned.squeezeX < 1, "a turned head should foreshorten the frame");
    assert.equal(straight.squeezeX, 1);
  });

  it("recovers the true scale from a foreshortened pupil span", () => {
    // Turning the head brings the pupils closer together in the image. Taken at
    // face value that would read as "further away" and shrink the frame; the
    // foreshortening correction is what stops it.
    const yaw = 25;
    const cos = Math.cos((yaw * Math.PI) / 180);
    const straight = fit({ spanPx: 310, faceOver: { yawDeg: 0 } });
    const turned = fit({ spanPx: 310 * cos, faceOver: { yawDeg: yaw } });

    assert.ok(
      Math.abs(turned.pixelsPerMm - straight.pixelsPerMm) < 1e-9,
      `${turned.pixelsPerMm} vs ${straight.pixelsPerMm}`,
    );
  });

  it("slides the bridge across the face as the head turns", () => {
    const left = fit({ faceOver: { yawDeg: -25 } }).transform.translateX;
    const centre = fit({ faceOver: { yawDeg: 0 } }).transform.translateX;
    const right = fit({ faceOver: { yawDeg: 25 } }).transform.translateX;

    assert.ok(left < centre && centre < right);
  });
});

describe("what the customer is told", () => {
  it("reports the frame's own lens-centre distance, not the wearer's PD", () => {
    // 52 mm lens + 18 mm bridge — a fixed property of the frame.
    assert.equal(fit().frameCentreDistanceMm, 70);
    assert.equal(fit({ pdMm: 68 }).frameCentreDistanceMm, 70);
  });

  it("reports decentration as half the difference, shared between the eyes", () => {
    assert.equal(fit({ pdMm: 62 }).decentrationMm, 4);
    assert.equal(fit({ pdMm: 70 }).decentrationMm, 0);
    assert.equal(fit({ pdMm: 76 }).decentrationMm, -3);
  });

  it("mentions decentration only once it is worth mentioning", () => {
    assert.ok(fit({ pdMm: 58 }).notes.some((n) => n.includes("lens centres")));
    assert.ok(!fit({ pdMm: 70 }).notes.some((n) => n.includes("lens centres")));
  });

  it("measures the face on the same PD-derived scale as the frame", () => {
    // Face edges at ±310 px is 620 px across; at 5 px/mm that is 124 mm.
    assert.equal(fit().faceWidthMm, 124);
  });

  it("calls a frame wider than the face wide, and a narrow one narrow", () => {
    assert.equal(fit({ physical: { ...RAVI, totalWidthMm: 145 } }).verdict, "wide");
    assert.equal(fit({ physical: { ...RAVI, totalWidthMm: 112 } }).verdict, "narrow");
    assert.equal(fit({ physical: { ...RAVI, totalWidthMm: 124 } }).verdict, "good");
  });

  it("says it does not know when the face edges were not found", () => {
    const f = fit({ faceOver: { leftFaceEdge: null, rightFaceEdge: null } });
    assert.equal(f.verdict, "unknown");
    assert.equal(f.faceWidthMm, null);
  });

  it("says so when it had to guess the overall width", () => {
    const f = fit({ physical: { ...RAVI, totalWidthMm: null } });
    assert.equal(f.frameWidthMm, 122); // 52 + 18 + 52
    assert.ok(f.notes.some((n) => n.includes("hasn't been recorded")));
  });

  it("falls back to the pupils, and says so, when nothing is measured", () => {
    const f = fit({ physical: {} });
    assert.equal(f.frameWidthMm, null);
    assert.ok(f.notes.some((n) => n.includes("no measurements")));
    // The fallback must still land the lens centres on the pupils.
    const centreSpan = (SNUG.rightLensCenterX - SNUG.leftLensCenterX) * ART_W * f.transform.scale;
    assert.ok(Math.abs(centreSpan - 310) < 1e-9);
  });
});

describe("every frame in the seeded catalogue", () => {
  // Ten shapes, ten different sets of measurements. None of them may need a
  // special case in the engine, which is what this pins.
  const CATALOGUE: Array<[string, FramePhysical]> = [
    ["Ravi (rectangle)", { lensWidthMm: 52, bridgeWidthMm: 18, templeLengthMm: 140, lensHeightMm: 37, totalWidthMm: 138 }],
    ["Noor (round)", { lensWidthMm: 47, bridgeWidthMm: 21, templeLengthMm: 145, lensHeightMm: 34, totalWidthMm: 133 }],
    ["Zara (cat eye)", { lensWidthMm: 53, bridgeWidthMm: 16, templeLengthMm: 140, lensHeightMm: 38, totalWidthMm: 136 }],
    ["Falcon (aviator)", { lensWidthMm: 58, bridgeWidthMm: 14, templeLengthMm: 140, lensHeightMm: 42, totalWidthMm: 144 }],
    ["Harbour (wayfarer)", { lensWidthMm: 54, bridgeWidthMm: 18, templeLengthMm: 145, lensHeightMm: 39, totalWidthMm: 142 }],
    ["Atlas (square)", { lensWidthMm: 55, bridgeWidthMm: 17, templeLengthMm: 145, lensHeightMm: 40, totalWidthMm: 145 }],
    ["Lyra (oval)", { lensWidthMm: 51, bridgeWidthMm: 19, templeLengthMm: 140, lensHeightMm: 37, totalWidthMm: 134 }],
    ["Vector (geometric)", { lensWidthMm: 50, bridgeWidthMm: 20, templeLengthMm: 145, lensHeightMm: 36, totalWidthMm: 137 }],
    ["Clark (browline)", { lensWidthMm: 52, bridgeWidthMm: 19, templeLengthMm: 145, lensHeightMm: 37, totalWidthMm: 140 }],
    ["Wren (rimless)", { lensWidthMm: 49, bridgeWidthMm: 20, templeLengthMm: 140, lensHeightMm: 35, totalWidthMm: 130 }],
  ];

  it("is drawn at its own recorded width", () => {
    for (const [name, physical] of CATALOGUE) {
      const f = fit({ physical });
      assert.equal(
        Math.round(drawnWidthPx(f)),
        Math.round(physical.totalWidthMm! * 5),
        `${name} drew at the wrong width`,
      );
    }
  });

  it("orders them on screen exactly as their millimetres order them", () => {
    const drawn = CATALOGUE.map(([name, p]) => [name, drawnWidthPx(fit({ physical: p }))] as const);
    const byDrawn = [...drawn].sort((a, b) => a[1] - b[1]).map(([n]) => n);
    const byMm = [...CATALOGUE].sort((a, b) => a[1].totalWidthMm! - b[1].totalWidthMm!).map(([n]) => n);
    assert.deepEqual(byDrawn, byMm);
  });

  it("passes its data check", () => {
    for (const [name, physical] of CATALOGUE) {
      const check = checkFrameData(physical, SNUG, ART_W, ART_H, true);
      assert.equal(check.canFitPhysically, true, `${name}: ${check.issues.map((i) => i.message).join(" ")}`);
    }
  });

  it("works across every PD in the range, for every frame", () => {
    for (const [name, physical] of CATALOGUE) {
      for (const pd of [54, 58, 62, 66, 70, 74]) {
        const f = fit({ physical, pdMm: pd });
        assert.ok(Number.isFinite(f.transform.scale) && f.transform.scale > 0, `${name} at PD ${pd}`);
        assert.equal(f.frameWidthMm, physical.totalWidthMm, `${name} at PD ${pd} changed size`);
      }
    }
  });
});

describe("PD validation", () => {
  it("accepts the ordinary range without comment", () => {
    for (const pd of [56, 62, 68, 74]) {
      const check = checkPd(pd);
      assert.equal(check.ok, true);
      assert.equal(check.unusual, false);
      assert.equal(check.message, null);
    }
  });

  it("accepts an unusual but possible PD, while asking the wearer to look again", () => {
    const low = checkPd(PD_TYPICAL_MIN_MM - 3);
    assert.equal(low.ok, true);
    assert.equal(low.unusual, true);
    assert.match(low.message!, /check your PD/i);

    assert.equal(checkPd(PD_TYPICAL_MAX_MM + 3).ok, true);
  });

  it("rejects a figure no pair of eyes could produce", () => {
    assert.equal(checkPd(6.2).ok, false);
    assert.equal(checkPd(620).ok, false);
    assert.match(checkPd(620).message!, /can be apart/);
  });

  it("treats a missing PD as absent rather than wrong", () => {
    for (const v of [null, undefined, NaN]) {
      const check = checkPd(v as number | null);
      assert.equal(check.ok, false);
      assert.equal(check.message, null);
    }
  });

  it("never claims a plausible PD is correct", () => {
    // Optical safety: this is a plausibility check, not a measurement.
    assert.equal(checkPd(62).message, null);
  });
});

describe("PD confidence", () => {
  it("says so when the entered and measured figures agree", () => {
    assert.equal(comparePd(62, 63.5, 0.8).agreement, "agrees");
  });

  it("flags a real disagreement without overriding what the customer entered", () => {
    const { agreement, message } = comparePd(62, 71, 0.8);
    assert.equal(agreement, "differs");
    assert.match(message!, /keep using the PD you entered/);
  });

  it("stays silent when the measurement itself is not trustworthy", () => {
    assert.equal(comparePd(62, 71, 0.2).agreement, "unknown");
    assert.equal(comparePd(62, null, 0.9).agreement, "unknown");
    assert.equal(comparePd(null, 71, 0.9).agreement, "unknown");
  });
});

describe("frame data quality", () => {
  it("blocks a colourway with no artwork", () => {
    const check = checkFrameData(RAVI, SNUG, ART_W, ART_H, false);
    assert.equal(check.canFitPhysically, false);
    assert.ok(check.issues.some((i) => i.field === "TryOnImageUrl"));
  });

  it("blocks a frame with no lens or bridge width", () => {
    assert.equal(checkFrameData({ totalWidthMm: 138 }, SNUG, ART_W, ART_H, true).canFitPhysically, false);
  });

  it("catches a frame narrower overall than its own lenses and bridge", () => {
    const check = checkFrameData(
      { lensWidthMm: 52, bridgeWidthMm: 18, totalWidthMm: 110 }, SNUG, ART_W, ART_H, true);
    assert.equal(check.canFitPhysically, false);
    assert.ok(check.issues.some((i) => i.message.includes("One of the three is wrong")));
  });

  it("catches a lens height entered in the wrong unit", () => {
    const check = checkFrameData({ ...RAVI, lensHeightMm: 3.7 }, SNUG, ART_W, ART_H, true);
    assert.ok(check.issues.some((i) => i.field === "LensHeightMm"));
  });

  it("warns rather than blocks when only the overall width is missing", () => {
    const check = checkFrameData({ ...RAVI, totalWidthMm: null }, SNUG, ART_W, ART_H, true);
    assert.equal(check.canFitPhysically, true);
    assert.ok(check.issues.some((i) => i.severity === "warning" && i.field === "TotalWidthMm"));
  });

  it("reads the same scale three ways from a correct asset", () => {
    // A calibration built to match Ravi exactly: 138 mm across 1000 px is
    // 7.246 px/mm, so the lens centres are 70 mm = 507 px apart and the 37 mm
    // aperture is 268 px deep on a 420 px canvas.
    const ppm = ART_W / 138;
    const centreFraction = (70 * ppm) / ART_W;
    const apertureFraction = (37 * ppm) / ART_H;
    const exact: FrameCalibration = {
      leftLensCenterX: 0.5 - centreFraction / 2,
      rightLensCenterX: 0.5 + centreFraction / 2,
      leftLensCenterY: 0.5, rightLensCenterY: 0.5,
      frontLeftX: 0, frontRightX: 1,
      lensTopY: 0.5 - apertureFraction / 2,
      lensBottomY: 0.5 + apertureFraction / 2,
    };

    const check = checkFrameData(RAVI, exact, ART_W, ART_H, true);
    assert.ok(check.spread !== null && check.spread < 1e-9, `spread ${check.spread}`);
    assert.ok(!check.issues.some((i) => i.field === "calibration"));
  });

  it("notices when the artwork and the measurements describe different frames", () => {
    // The shipped generic artwork against Ravi's real numbers: lens centres at
    // 0.29/0.71 imply a much wider frame than 138 mm does.
    const check = checkFrameData(RAVI, DEFAULT_CALIBRATION, ART_W, ART_H, true);
    assert.ok(check.spread !== null && check.spread > CALIBRATION_TOLERANCE);
    assert.ok(check.issues.some((i) => i.field === "calibration"));
    // It is a warning: an approximate frame still beats no try-on at all.
    assert.equal(check.canFitPhysically, true);
  });
});

describe("calibration from the legacy anchor columns", () => {
  it("reads the two anchors as the two lens centres", () => {
    const cal = calibrationFrom({ leftX: 0.3, leftY: 0.48, rightX: 0.7, rightY: 0.52 });
    assert.equal(cal.leftLensCenterX, 0.3);
    assert.equal(cal.rightLensCenterY, 0.52);
  });

  it("falls back to the generated artwork's bounds when none are recorded", () => {
    const cal = calibrationFrom({ leftX: 0.29, leftY: 0.5, rightX: 0.71, rightY: 0.5 }, null);
    assert.equal(cal.frontLeftX, DEFAULT_CALIBRATION.frontLeftX);
    assert.equal(cal.lensBottomY, DEFAULT_CALIBRATION.lensBottomY);
  });

  it("prefers measured bounds over the defaults", () => {
    const cal = calibrationFrom(
      { leftX: 0.29, leftY: 0.5, rightX: 0.71, rightY: 0.5 },
      { frontLeftX: 0.1, frontRightX: 0.9 },
    );
    assert.equal(cal.frontLeftX, 0.1);
    assert.equal(cal.frontRightX, 0.9);
    assert.equal(cal.lensTopY, DEFAULT_CALIBRATION.lensTopY);
  });
});

describe("manual adjustment", () => {
  it("applies on top of the automatic fit rather than replacing it", () => {
    const auto = fit();
    const nudged = solveFit({
      face: face(310), pdMm: 62, pdSource: "entered", physical: RAVI, calibration: SNUG,
      artworkWidth: ART_W, artworkHeight: ART_H,
      manual: { scale: 1.1, rotate: 0.05, offsetX: 12, offsetY: -8 },
    })!;

    assert.ok(Math.abs(nudged.transform.scale - auto.transform.scale * 1.1) < 1e-9);
    assert.ok(Math.abs(nudged.transform.rotate - (auto.transform.rotate + 0.05)) < 1e-9);
    assert.equal(nudged.transform.translateX, auto.transform.translateX + 12);
    assert.equal(nudged.transform.translateY, auto.transform.translateY - 8);
  });

  it("leaves the reported measurements untouched — a nudge is not a measurement", () => {
    const nudged = solveFit({
      face: face(310), pdMm: 62, pdSource: "entered", physical: RAVI, calibration: SNUG,
      artworkWidth: ART_W, artworkHeight: ART_H,
      manual: { scale: 1.3, rotate: 0, offsetX: 0, offsetY: 0 },
    })!;
    assert.equal(nudged.frameWidthMm, 138);
    assert.equal(nudged.pixelsPerMm, 5);
  });
});
