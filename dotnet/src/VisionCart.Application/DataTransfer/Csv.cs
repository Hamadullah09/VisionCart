using System.Globalization;
using System.Text;

namespace VisionCart.Application.DataTransfer;

/// <summary>
/// Port of <c>src/lib/csv.ts</c>.
///
/// Small and dependency-free, and it handles the things a real export from Excel
/// or Google Sheets actually contains: quoted fields, embedded commas and
/// newlines, doubled quotes, CRLF line endings and a UTF-8 byte-order mark.
/// </summary>
public static class Csv
{
    /// <summary>Excel writes a BOM; leaving it in corrupts the first header name.</summary>
    private const char Bom = '﻿';

    public static List<List<string>> Parse(string input)
    {
        var text = input.Length > 0 && input[0] == Bom ? input[1..] : input;

        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // A doubled quote inside a quoted field is a literal quote.
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\n':
                    row.Add(field.ToString());
                    rows.Add(row);
                    row = [];
                    field.Clear();
                    break;
                case '\r':
                    // Swallowed; the \n that follows ends the row.
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        // A file that doesn't end with a newline still has a last row.
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return [.. rows.Where(r => r.Any(cell => !string.IsNullOrWhiteSpace(cell)))];
    }

    /// <summary>Parse into dictionaries keyed by the header row, trimmed and lower-cased.</summary>
    public static List<Dictionary<string, string>> ParseObjects(string input)
    {
        var rows = Parse(input);
        if (rows.Count < 2) return [];

        var headers = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();

        return
        [
            .. rows.Skip(1).Select(row =>
            {
                var obj = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < headers.Count; i++)
                    obj[headers[i]] = (i < row.Count ? row[i] : string.Empty).Trim();
                return obj;
            }),
        ];
    }

    public static string Write(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<string>? columns = null)
    {
        // A dataset with no rows must still produce its header. A brand-new shop
        // exporting patients would otherwise get a completely empty file and have
        // no column names to fill in — which defeats the export/edit/import round
        // trip the format exists for. Only possible when the caller declares the
        // columns, since with no rows there is nothing to infer them from.
        if (rows.Count == 0 && columns is not { Count: > 0 }) return string.Empty;

        var cols = columns ?? [.. rows[0].Keys];
        var builder = new StringBuilder();

        builder.Append(Bom); // Makes Excel open UTF-8 correctly instead of mangling accents.
        builder.Append(string.Join(",", cols));

        foreach (var row in rows)
        {
            builder.Append("\r\n");
            builder.Append(string.Join(",", cols.Select(c => Escape(row.GetValueOrDefault(c)))));
        }

        return builder.ToString();
    }

    private static string Escape(object? value)
    {
        if (value is null) return string.Empty;

        var s = value switch
        {
            DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

        // Quote anything a spreadsheet could misread, and double any inner quotes.
        return s.AsSpan().IndexOfAny('"', ',', '\r') >= 0 || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;
    }

    /// <summary>
    /// Tolerates the shapes spreadsheets actually produce: a leading plus on a
    /// diopter, thousands separators, and stray whitespace.
    /// </summary>
    public static double? Number(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var cleaned = value.Replace(",", string.Empty).Replace(" ", string.Empty).TrimStart('+');

        if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            throw new FormatException($"\"{value}\" is not a number.");

        return n;
    }

    public static int? Integer(string? value) =>
        Number(value) is { } n ? (int)Math.Truncate(n) : null;

    public static DateTime? Date(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
        {
            throw new FormatException($"{field} \"{value}\" is not a valid date (use YYYY-MM-DD).");
        }

        return date;
    }

    public static bool Flag(string? value) =>
        value is not null && value.Trim().ToLowerInvariant() is "yes" or "true" or "1" or "y";
}
