/**
 * Small, dependency-free CSV handling for the back office.
 *
 * Handles the things a real export from Excel or Google Sheets actually
 * contains: quoted fields, embedded commas and newlines, doubled quotes, CRLF
 * line endings and a UTF-8 BOM.
 */

export function parseCsv(input: string): string[][] {
  // Excel writes a BOM; leaving it in corrupts the first header name.
  const text = input.replace(/^﻿/, "");
  const rows: string[][] = [];
  let row: string[] = [];
  let field = "";
  let inQuotes = false;

  for (let i = 0; i < text.length; i++) {
    const c = text[i];

    if (inQuotes) {
      if (c === '"') {
        if (text[i + 1] === '"') {
          field += '"';
          i++;
        } else {
          inQuotes = false;
        }
      } else {
        field += c;
      }
      continue;
    }

    if (c === '"') {
      inQuotes = true;
    } else if (c === ",") {
      row.push(field);
      field = "";
    } else if (c === "\n") {
      row.push(field);
      rows.push(row);
      row = [];
      field = "";
    } else if (c === "\r") {
      // Swallowed; the \n that follows ends the row.
    } else {
      field += c;
    }
  }

  // A file that doesn't end with a newline still has a last row.
  if (field.length > 0 || row.length > 0) {
    row.push(field);
    rows.push(row);
  }

  return rows.filter((r) => r.some((cell) => cell.trim().length > 0));
}

/** Parse into objects keyed by the header row, trimmed and lower-cased. */
export function parseCsvObjects(input: string): Record<string, string>[] {
  const rows = parseCsv(input);
  if (rows.length < 2) return [];

  const headers = rows[0].map((h) => h.trim().toLowerCase());
  return rows.slice(1).map((row) => {
    const obj: Record<string, string> = {};
    headers.forEach((h, i) => {
      obj[h] = (row[i] ?? "").trim();
    });
    return obj;
  });
}

export function toCsv(rows: Record<string, unknown>[], columns?: string[]): string {
  if (rows.length === 0) return "";
  const cols = columns ?? Object.keys(rows[0]);

  const escape = (value: unknown): string => {
    if (value === null || value === undefined) return "";
    const s = value instanceof Date ? value.toISOString() : String(value);
    // Quote anything a spreadsheet could misread, and double any inner quotes.
    return /[",\r\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
  };

  const lines = [cols.join(",")];
  for (const row of rows) {
    lines.push(cols.map((c) => escape(row[c])).join(","));
  }
  // The BOM makes Excel open UTF-8 correctly instead of mangling accents.
  return "﻿" + lines.join("\r\n");
}

export function csvResponse(filename: string, csv: string): Response {
  return new Response(csv, {
    headers: {
      "Content-Type": "text/csv; charset=utf-8",
      "Content-Disposition": `attachment; filename="${filename}"`,
      "Cache-Control": "no-store",
    },
  });
}
