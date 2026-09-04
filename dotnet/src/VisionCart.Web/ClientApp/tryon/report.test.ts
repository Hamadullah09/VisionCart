import { describe, it } from "node:test";
import assert from "node:assert/strict";

import { formatFitReport, type FitReport } from "./report.ts";

const CAVEAT =
  "These are estimates from a photograph, not a clinical measurement. " +
  "Our optician checks them before your lenses are cut.";

/** The panel as it stands for a photo the detector could not measure. */
function report(over: Partial<FitReport> = {}): FitReport {
  return {
    title: "How this frame fits you",
    storeName: "VisionCart Optical",
    frame: "Meridian · Ravi · Matte Black",
    dateText: "4 September 2026",
    sections: [
      {
        heading: null,
        rows: [
          { label: "Frame width", value: "138 mm across" },
          { label: "Lens size", value: "52 × 37 mm" },
          { label: "Decentration", value: "6 mm per eye" },
        ],
      },
      {
        heading: "Measured from your photo",
        rows: [{ label: "Pupillary distance", value: "58.0 mm" }],
      },
    ],
    notes: [
      "Your eyes sit 6 mm inside this frame's lens centres. Your lenses will be " +
      "ground to match, which makes them a little thicker at the edge.",
    ],
    caveat: CAVEAT,
    ...over,
  };
}

const lines = (text: string): string[] => text.split("\r\n");

describe("fit report", () => {
  it("carries every measurement the panel showed", () => {
    const text = formatFitReport(report());

    assert.match(text, /Frame width\s+138 mm across/);
    assert.match(text, /Lens size\s+52 × 37 mm/);
    assert.match(text, /Decentration\s+6 mm per eye/);
    assert.match(text, /Pupillary distance\s+58\.0 mm/);
  });

  it("names the shop, the frame and the day", () => {
    const text = formatFitReport(report());

    assert.match(text, /VisionCart Optical/);
    assert.match(text, /Meridian · Ravi · Matte Black/);
    assert.match(text, /4 September 2026/);
  });

  it("keeps the headings that separate frame from face", () => {
    const text = formatFitReport(report());

    assert.match(text, /How this frame fits you/);
    assert.match(text, /Measured from your photo/);
  });

  it("writes the caveat, which is the point of the file leaving the page", () => {
    const text = formatFitReport(report());
    const flattened = text.replace(/\r\n/g, " ").replace(/\s+/g, " ");

    assert.ok(flattened.includes("not a clinical measurement"));
    assert.ok(flattened.includes("optician checks them before your lenses are cut"));
  });

  it("keeps the decentration note, marked as a note", () => {
    const text = formatFitReport(report());

    assert.match(text, /\* Your eyes sit 6 mm inside/);
  });

  it("lines the values up in one column across every section", () => {
    const measures = lines(formatFitReport(report()))
      .filter(line => /^(Frame width|Lens size|Decentration|Pupillary distance)/.test(line));

    assert.equal(measures.length, 4);

    // The value begins after the run of padding that separates it from the label.
    const valueColumn = (line: string): number => {
      const gap = /\s{2,}/.exec(line);
      return gap ? gap.index + gap[0].length : -1;
    };

    const columns = new Set(measures.map(valueColumn));
    assert.equal(columns.size, 1, `values start in different columns:\n${measures.join("\n")}`);
  });

  it("wraps prose but never a measurement row", () => {
    for (const line of lines(formatFitReport(report()))) {
      assert.ok(line.length <= 78, `line runs to ${line.length}: ${line}`);
    }
  });

  it("ends with CRLF throughout, for a file opened in Notepad", () => {
    const text = formatFitReport(report());

    assert.ok(text.endsWith("\r\n"));
    assert.equal(text.replace(/\r\n/g, "").includes("\n"), false, "a bare newline got through");
  });

  it("drops a section the panel had nothing to put in", () => {
    const text = formatFitReport(report({
      sections: [
        { heading: null, rows: [{ label: "Frame width", value: "138 mm across" }] },
        { heading: "Measured from your photo", rows: [] },
      ],
    }));

    assert.doesNotMatch(text, /Measured from your photo/);
  });

  it("stands up without the optional parts", () => {
    const text = formatFitReport({
      title: "How this frame fits you",
      sections: [{ heading: null, rows: [{ label: "Frame width", value: "138 mm across" }] }],
    });

    assert.match(text, /How this frame fits you/);
    assert.match(text, /Frame width\s+138 mm across/);
  });
});
