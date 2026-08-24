using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using VisionCart.Application.Common;
using VisionCart.Application.Patients;
using VisionCart.Application.Platform;
using VisionCart.Application.Pricing;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;
using VisionCart.Domain.ValueObjects;

namespace VisionCart.Application.DataTransfer;

public sealed record RowError(int Row, string Message);

public sealed class ImportOutcome
{
    public string? JobId { get; init; }
    public bool DryRun { get; init; }
    public int Total { get; init; }
    public int Ok { get; init; }
    public int Failed { get; init; }
    public IReadOnlyList<RowError> Errors { get; init; } = [];
    public string? FatalError { get; init; }

    public bool IsFatal => FatalError is not null;

    public static ImportOutcome Fatal(string message) => new() { FatalError = message };
}

public interface IImportService
{
    IReadOnlyList<(string Key, string Label)> Kinds { get; }
    Task<ImportOutcome> ImportAsync(string kind, string filename, string content, bool dryRun,
        string? userId, CancellationToken ct = default);
    Task<IReadOnlyList<ImportJob>> RecentJobsAsync(int take = 15, CancellationToken ct = default);
}

/// <summary>
/// Port of <c>src/app/api/admin/import/route.ts</c>.
///
/// Two rules carried over, both of which protect production data:
///
/// 1. <b>A check runs first.</b> Nothing is written until staff press "import for
///    real"; the dry run reports bad rows by line number so a broken file is
///    fixed before it touches the catalogue.
/// 2. <b>Rows are matched, not appended.</b> Frames on <c>variant_sku</c> and
///    patients on <c>file_no</c>, so re-importing an edited export updates rather
///    than duplicating — which is what makes exports round-trippable.
///
/// Uploaded spreadsheet data is never trusted: every row is validated, and a row
/// that throws is recorded against its line number while the rest still go through.
/// </summary>
public sealed class ImportService(
    IApplicationDbContext db,
    IPatientService patients,
    IAuditService audit,
    IOptions<StoreOptions> store,
    TimeProvider clock,
    ILogger<ImportService> logger) : IImportService
{
    private const int MaxReportedErrors = 200;

    private readonly string _currency = store.Value.Currency;

    public IReadOnlyList<(string Key, string Label)> Kinds =>
    [
        ("frames", "Frames & stock"),
        ("patients", "Patients & prescriptions"),
    ];

    public async Task<ImportOutcome> ImportAsync(
        string kind, string filename, string content, bool dryRun, string? userId,
        CancellationToken ct = default)
    {
        if (!Kinds.Any(k => k.Key == kind))
            return ImportOutcome.Fatal($"Unknown import type \"{kind}\".");

        List<Dictionary<string, string>> rows;
        try
        {
            rows = Csv.ParseObjects(content);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not parse import file {Filename}", filename);
            return ImportOutcome.Fatal("That file could not be read as a CSV.");
        }

        if (rows.Count == 0)
        {
            return ImportOutcome.Fatal(
                "No data rows found. The first line must be the column headers.");
        }

        var now = clock.GetUtcNow().UtcDateTime;

        var job = new ImportJob
        {
            Kind = kind,
            Filename = filename,
            Status = ImportJobStatuses.Running,
            TotalRows = rows.Count,
            CreatedBy = userId,
            IsDryRun = dryRun,
        };
        db.ImportJobs.Add(job);
        await db.SaveChangesAsync(ct);

        var errors = new List<RowError>();
        var ok = 0;

        for (var i = 0; i < rows.Count; i++)
        {
            // +1 for the header, +1 for 1-based line numbers: the number staff
            // will see in their spreadsheet.
            var line = i + 2;

            try
            {
                if (kind == "frames") await ImportFrameRowAsync(rows[i], dryRun, ct);
                else await ImportPatientRowAsync(rows[i], dryRun, now, ct);
                ok++;
            }
            catch (Exception ex)
            {
                errors.Add(new RowError(line, ex.Message));
            }
        }

        job.Status = errors.Count == rows.Count ? ImportJobStatuses.Failed : ImportJobStatuses.Completed;
        job.OkRows = ok;
        job.ErrorRows = errors.Count;
        job.Report = JsonSerializer.Serialize(errors.Take(MaxReportedErrors));
        job.FinishedAt = clock.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(ct);

        if (!dryRun)
        {
            await audit.WriteAsync("data.import", "ImportJob", job.Id,
                new { kind, ok, failed = errors.Count }, ct);
        }

        return new ImportOutcome
        {
            JobId = job.Id,
            DryRun = dryRun,
            Total = rows.Count,
            Ok = ok,
            Failed = errors.Count,
            Errors = [.. errors.Take(50)],
        };
    }

    public async Task<IReadOnlyList<ImportJob>> RecentJobsAsync(
        int take = 15, CancellationToken ct = default) =>
        await db.ImportJobs.AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    // --- Frames -------------------------------------------------------------

    private async Task ImportFrameRowAsync(
        Dictionary<string, string> row, bool dryRun, CancellationToken ct)
    {
        var variantSku = Value(row, "variant_sku") ?? Value(row, "sku");
        var frameSku = Value(row, "frame_sku") ?? variantSku;
        var name = Value(row, "frame_name") ?? Value(row, "name");

        if (variantSku is null) throw new InvalidOperationException("variant_sku is required.");
        if (name is null) throw new InvalidOperationException("frame_name is required.");

        var price = Csv.Number(row.GetValueOrDefault("price"))
            ?? throw new InvalidOperationException("price is required and must be a number.");

        // Validate everything that can fail before writing anything, so the dry
        // run reports the same errors the real import would hit.
        var totalWidth = Csv.Number(row.GetValueOrDefault("total_width_mm"));
        var stock = Csv.Integer(row.GetValueOrDefault("stock_qty"));
        var compareAt = Csv.Number(row.GetValueOrDefault("compare_at"));
        var cost = Csv.Number(row.GetValueOrDefault("cost"));

        var status = Value(row, "status") ?? ProductStatuses.Draft;
        if (!ProductStatuses.All.Contains(status))
            throw new InvalidOperationException($"status \"{status}\" is not draft, active or archived.");

        if (dryRun) return;

        string? brandId = null;
        if (Value(row, "brand") is { } brandName)
        {
            var brand = await db.Brands.FirstOrDefaultAsync(b => b.Name == brandName, ct);
            if (brand is null)
            {
                brand = new Brand { Name = brandName, Slug = Slugify(brandName) };
                db.Brands.Add(brand);
                await db.SaveChangesAsync(ct);
            }
            brandId = brand.Id;
        }

        var frame = await db.Frames.FirstOrDefaultAsync(f => f.Sku == frameSku, ct);
        if (frame is null)
        {
            frame = new Frame { Sku = frameSku!, Slug = Slugify(name) };
            db.Frames.Add(frame);
        }

        frame.Name = name;
        frame.BrandId = brandId ?? frame.BrandId;
        frame.Shape = Value(row, "shape") ?? frame.Shape;
        frame.Material = Value(row, "material") ?? frame.Material;
        frame.RimType = Value(row, "rim_type") ?? frame.RimType;
        frame.Gender = Value(row, "gender") ?? frame.Gender;
        frame.LensWidthMm = Csv.Number(row.GetValueOrDefault("lens_width_mm")) ?? frame.LensWidthMm;
        frame.BridgeWidthMm = Csv.Number(row.GetValueOrDefault("bridge_width_mm")) ?? frame.BridgeWidthMm;
        frame.TempleLengthMm = Csv.Number(row.GetValueOrDefault("temple_length_mm")) ?? frame.TempleLengthMm;
        frame.TotalWidthMm = totalWidth ?? frame.TotalWidthMm;
        frame.SizeBand = Band(frame.TotalWidthMm) ?? frame.SizeBand;
        frame.BasePriceMinor = Money.ToMinor((decimal)price, _currency);
        frame.CompareAtMinor = compareAt is { } c ? Money.ToMinor((decimal)c, _currency) : frame.CompareAtMinor;
        frame.CostMinor = cost is { } cst ? Money.ToMinor((decimal)cst, _currency) : frame.CostMinor;
        frame.Status = status;
        frame.SearchText = $"{frame.Name} {frame.Shape} {frame.Material} {frame.Gender}".ToLowerInvariant();

        await db.SaveChangesAsync(ct);

        // Matched on variant_sku — this is what makes an edited export update
        // rather than duplicate.
        var variant = await db.FrameVariants.FirstOrDefaultAsync(v => v.Sku == variantSku, ct);
        if (variant is null)
        {
            variant = new FrameVariant
            {
                Sku = variantSku!,
                FrameId = frame.Id,
                ColorName = Value(row, "color_name") ?? "Default",
            };
            db.FrameVariants.Add(variant);
        }

        variant.ColorName = Value(row, "color_name") ?? variant.ColorName;
        variant.ColorHex = Value(row, "color_hex") ?? variant.ColorHex;
        variant.Barcode = Value(row, "barcode") ?? variant.Barcode;
        variant.StockQty = stock ?? variant.StockQty;
        variant.TryOnImageUrl = Value(row, "try_on_image") ?? variant.TryOnImageUrl;

        await db.SaveChangesAsync(ct);
    }

    // --- Patients -----------------------------------------------------------

    private async Task ImportPatientRowAsync(
        Dictionary<string, string> row, bool dryRun, DateTime now, CancellationToken ct)
    {
        var firstName = Value(row, "first_name") ?? Value(row, "firstname");
        if (firstName is null) throw new InvalidOperationException("first_name is required.");

        var dob = Csv.Date(row.GetValueOrDefault("date_of_birth"), "date_of_birth");
        var issued = Csv.Date(row.GetValueOrDefault("issued"), "issued");
        var expires = Csv.Date(row.GetValueOrDefault("expires"), "expires");
        var pd = Csv.Number(row.GetValueOrDefault("pd_mm"));

        var odSphere = Csv.Number(row.GetValueOrDefault("od_sphere"));
        var odCylinder = Csv.Number(row.GetValueOrDefault("od_cylinder"));
        var odAxis = Csv.Integer(row.GetValueOrDefault("od_axis"));
        var osSphere = Csv.Number(row.GetValueOrDefault("os_sphere"));
        var osCylinder = Csv.Number(row.GetValueOrDefault("os_cylinder"));
        var osAxis = Csv.Integer(row.GetValueOrDefault("os_axis"));

        // The same clinical rule the customer form and the optician form enforce:
        // a cylinder is meaningless without the axis it sits on.
        if (odCylinder is not null and not 0 && odAxis is null)
            throw new InvalidOperationException("od_axis is required when od_cylinder is given.");
        if (osCylinder is not null and not 0 && osAxis is null)
            throw new InvalidOperationException("os_axis is required when os_cylinder is given.");

        foreach (var (value, field) in new[]
                 {
                     (odSphere, "od_sphere"), (osSphere, "os_sphere"),
                     (odCylinder, "od_cylinder"), (osCylinder, "os_cylinder"),
                 })
        {
            if (value is { } v && !Diopters.IsQuarterStep(v))
                throw new InvalidOperationException($"{field} \"{v}\" is not a 0.25 D step — no lab can make it.");
        }

        if (dryRun) return;

        var fileNo = Value(row, "file_no");

        var patient = fileNo is null
            ? null
            : await db.Patients.FirstOrDefaultAsync(p => p.FileNo == fileNo, ct);

        if (patient is null)
        {
            patient = new Patient { FileNo = fileNo ?? await patients.NextFileNoAsync(ct) };
            db.Patients.Add(patient);
        }

        patient.FirstName = firstName;
        patient.LastName = Value(row, "last_name") ?? Value(row, "lastname") ?? patient.LastName;
        patient.Email = Value(row, "email")?.ToLowerInvariant() ?? patient.Email;
        patient.Phone = Value(row, "phone") ?? patient.Phone;
        patient.DateOfBirth = dob ?? patient.DateOfBirth;
        patient.PdMm = pd ?? patient.PdMm;
        patient.Notes = Value(row, "notes") ?? patient.Notes;
        patient.ConsentMarketing = Csv.Flag(row.GetValueOrDefault("marketing_consent"));

        await db.SaveChangesAsync(ct);

        // A prescription in the same row is imported alongside the file, which is
        // how most practice-management exports are shaped.
        var hasRx = odSphere is not null || osSphere is not null
                    || odCylinder is not null || osCylinder is not null;

        if (!hasRx) return;

        db.Prescriptions.Add(new Prescription
        {
            PatientId = patient.Id,
            Source = RxSources.Imported,
            // Imported prescriptions are never trusted straight into the lab.
            Status = RxStatuses.PendingVerification,
            IssuedAt = issued ?? now,
            ExpiresAt = expires,
            OdSphere = odSphere, OdCylinder = odCylinder, OdAxis = odAxis,
            OdAdd = Csv.Number(row.GetValueOrDefault("od_add")),
            OdPdMm = Csv.Number(row.GetValueOrDefault("od_pd")),
            OsSphere = osSphere, OsCylinder = osCylinder, OsAxis = osAxis,
            OsAdd = Csv.Number(row.GetValueOrDefault("os_add")),
            OsPdMm = Csv.Number(row.GetValueOrDefault("os_pd")),
            Prescriber = Value(row, "prescriber"),
            Notes = "Imported — verify before dispensing.",
        });

        await db.SaveChangesAsync(ct);
    }

    private static string? Value(Dictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static string? Band(double? totalWidthMm) => totalWidthMm switch
    {
        null => null,
        < 130 => SizeBands.Narrow,
        > 143 => SizeBands.Wide,
        _ => SizeBands.Medium,
    };

    private static string Slugify(string value)
    {
        var slug = new string([.. value.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]);
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        return slug.Length > 80 ? slug[..80] : slug;
    }
}
