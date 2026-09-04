import { describe, it } from "node:test";
import assert from "node:assert/strict";

import { snapshotFilename } from "./naming.ts";

describe("snapshot filename", () => {
  it("carries the frame and the PD it was drawn at", () => {
    assert.equal(
      snapshotFilename({ slug: "ravi", pdMm: 58, pdSource: "entered" }),
      "tryon-ravi-pd-58.jpg",
    );
  });

  it("writes a whole millimetre without a trailing zero", () => {
    assert.equal(
      snapshotFilename({ slug: "ravi", pdMm: 58.0, pdSource: "entered" }),
      "tryon-ravi-pd-58.jpg",
    );
  });

  it("keeps a half millimetre, which is how PDs are usually written", () => {
    assert.equal(
      snapshotFilename({ slug: "noor", pdMm: 62.5, pdSource: "entered" }),
      "tryon-noor-pd-62.5.jpg",
    );
  });

  it("rounds to the tenth the measurements panel displays", () => {
    assert.equal(
      snapshotFilename({ slug: "noor", pdMm: 61.34, pdSource: "entered" }),
      "tryon-noor-pd-61.3.jpg",
    );
  });

  it("marks a PD the detector guessed rather than one we were given", () => {
    assert.equal(
      snapshotFilename({ slug: "zara", pdMm: 61.5, pdSource: "estimated" }),
      "tryon-zara-pd-61.5-estimated.jpg",
    );
  });

  it("never lets an estimate pass as a measurement", () => {
    const entered = snapshotFilename({ slug: "zara", pdMm: 60, pdSource: "entered" });
    const estimated = snapshotFilename({ slug: "zara", pdMm: 60, pdSource: "estimated" });

    assert.notEqual(entered, estimated);
  });

  it("omits the PD entirely when there isn't one yet", () => {
    assert.equal(snapshotFilename({ slug: "ravi", pdMm: null }), "tryon-ravi.jpg");
    assert.equal(snapshotFilename({ slug: "ravi" }), "tryon-ravi.jpg");
  });

  it("omits a PD that is not a number a face can have", () => {
    for (const pdMm of [0, -58, Number.NaN, Number.POSITIVE_INFINITY]) {
      assert.equal(
        snapshotFilename({ slug: "ravi", pdMm, pdSource: "entered" }),
        "tryon-ravi.jpg",
        `PD ${pdMm} should not reach the filename`,
      );
    }
  });

  it("falls back to a generic name when no frame is selected", () => {
    assert.equal(snapshotFilename({ pdMm: 58, pdSource: "entered" }), "tryon-frame-pd-58.jpg");
    assert.equal(snapshotFilename({ slug: "", pdMm: 58 }), "tryon-frame-pd-58.jpg");
  });

  it("reduces a slug to characters a filename can hold anywhere", () => {
    assert.equal(
      snapshotFilename({ slug: "Ravi / Matte Black", pdMm: 58 }),
      "tryon-ravi-matte-black-pd-58.jpg",
    );
    assert.equal(snapshotFilename({ slug: "../../etc/passwd", pdMm: 58 }), "tryon-etc-passwd-pd-58.jpg");
  });
});
