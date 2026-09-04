/**
 * The fit panel, written out as something a customer can keep.
 *
 * A downloaded photograph shows a frame on a face; it does not carry the
 * numbers underneath it. Those numbers are the part an optician can act on —
 * the decentration especially, which decides how a lens is ground — so this
 * writes them into a plain text file that opens anywhere, needs no reader, and
 * can be forwarded to a practice that has never heard of us.
 *
 * The caveat is not optional and is not a footnote here either: everything in
 * the report came off a photograph, and the file says so in the same words the
 * screen does. Anyone reading it without the page in front of them needs that
 * more than the customer did, not less.
 *
 * Rendering only. What goes in the report is decided by the panel, which is
 * already the one place those values are formatted; this never recomputes a
 * measurement, so the file and the screen cannot drift apart. No DOM here, so
 * the layout is testable on its own.
 */

/** One measurement: the label the panel showed, and the value it showed. */
export interface ReportRow {
  label: string;
  value: string;
}

/** A headed group of measurements, mirroring one `<dl>` in the panel. */
export interface ReportSection {
  heading: string | null;
  rows: ReportRow[];
}

export interface FitReport {
  /** The panel's own title, e.g. "How this frame fits you". */
  title: string;
  /** The shop, so a forwarded file says where it came from. */
  storeName?: string | null;
  /** The frame it describes, e.g. "Ravi — Matte Black". */
  frame?: string | null;
  /** When it was produced, already formatted for the reader's locale. */
  dateText?: string | null;
  sections: ReportSection[];
  /** The panel's footnotes — decentration and the like. */
  notes?: string[];
  /** The standing caveat. Always written when the panel carries one. */
  caveat?: string | null;
}

/** Wrap width. Comfortable in Notepad at its default window size. */
const WRAP_COLUMNS = 76;

/** Gap between the widest label and the values, in spaces. */
const LABEL_GAP = 2;

/**
 * Render the report as plain text.
 *
 * CRLF throughout: this is a `.txt` a customer is most likely to open in
 * Notepad, and a file of bare newlines still lands there as one long line on
 * older builds. Every other reader copes with CRLF.
 */
export function formatFitReport(report: FitReport): string {
  const lines: string[] = [];

  if (report.storeName) lines.push(report.storeName);
  lines.push(report.title);
  lines.push("=".repeat(report.title.length));
  lines.push("");

  if (report.frame) lines.push(report.frame);
  if (report.dateText) lines.push(report.dateText);
  if (report.frame || report.dateText) lines.push("");

  // One column width across every section, so the values line up down the
  // whole page rather than stepping in and out at each heading.
  const width = labelWidth(report.sections);

  for (const section of report.sections) {
    if (section.rows.length === 0) continue;

    if (section.heading) {
      lines.push(section.heading);
      lines.push("-".repeat(section.heading.length));
    }

    for (const row of section.rows) {
      lines.push(`${row.label.padEnd(width)}${row.value}`);
    }

    lines.push("");
  }

  for (const note of report.notes ?? []) {
    // Continuation lines sit under the text, not under the bullet.
    lines.push(...wrap(note, WRAP_COLUMNS - 2).map((line, i) => (i === 0 ? `* ${line}` : `  ${line}`)));
    lines.push("");
  }

  if (report.caveat) {
    lines.push(...wrap(report.caveat, WRAP_COLUMNS));
    lines.push("");
  }

  return `${lines.join("\r\n").trimEnd()}\r\n`;
}

/** The column the values start in: the longest label, plus a gap. */
function labelWidth(sections: ReportSection[]): number {
  let longest = 0;
  for (const section of sections) {
    for (const row of section.rows) {
      if (row.label.length > longest) longest = row.label.length;
    }
  }
  return longest + LABEL_GAP;
}

/**
 * Greedy wrap on whitespace.
 *
 * A word longer than the column is left to overrun rather than broken — a
 * hyphenated measurement split across two lines would be worse than a long
 * line.
 */
function wrap(text: string, columns: number): string[] {
  const words = text.split(/\s+/).filter(Boolean);
  if (words.length === 0) return [];

  const lines: string[] = [];
  let line = "";

  for (const word of words) {
    if (line === "") {
      line = word;
    } else if (line.length + 1 + word.length <= columns) {
      line += ` ${word}`;
    } else {
      lines.push(line);
      line = word;
    }
  }

  if (line !== "") lines.push(line);
  return lines;
}
