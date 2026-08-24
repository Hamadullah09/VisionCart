using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using Microsoft.Extensions.DependencyInjection;
using VisionCart.Application.DataTransfer;
using VisionCart.Application.Media;
using VisionCart.Domain.Constants;
using VisionCart.Infrastructure.Persistence;

namespace VisionCart.IntegrationTests;

/// <summary>
/// CSV parsing, import, export and the media library.
///
/// The parser tests exercise the shapes a real spreadsheet export actually
/// contains — the ones that break naive `split(',')` implementations.
/// </summary>
public class CsvParserTests
{
    [Fact]
    public void Quoted_fields_may_contain_commas()
    {
        var rows = Csv.Parse("a,b\n\"one, two\",three");
        Assert.Equal(["one, two", "three"], rows[1]);
    }

    [Fact]
    public void A_doubled_quote_inside_a_quoted_field_is_a_literal_quote()
    {
        var rows = Csv.Parse("a\n\"He said \"\"hello\"\"\"");
        Assert.Equal("He said \"hello\"", rows[1][0]);
    }

    [Fact]
    public void Quoted_fields_may_span_lines()
    {
        var rows = Csv.Parse("a,b\n\"line one\nline two\",x");
        Assert.Equal(2, rows.Count);
        Assert.Equal("line one\nline two", rows[1][0]);
    }

    [Fact]
    public void Windows_line_endings_are_handled()
    {
        var rows = Csv.Parse("a,b\r\n1,2\r\n3,4");
        Assert.Equal(3, rows.Count);
        Assert.Equal(["3", "4"], rows[2]);
    }

    [Fact]
    public void A_utf8_byte_order_mark_does_not_corrupt_the_first_header()
    {
        // Excel writes one. Leaving it in makes the first column unfindable.
        var rows = Csv.ParseObjects("﻿file_no,first_name\nP-000001,Ada");
        Assert.True(rows[0].ContainsKey("file_no"));
        Assert.Equal("P-000001", rows[0]["file_no"]);
    }

    [Fact]
    public void A_file_with_no_trailing_newline_still_yields_its_last_row()
    {
        var rows = Csv.Parse("a,b\n1,2");
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Blank_lines_are_dropped()
    {
        var rows = Csv.Parse("a,b\n1,2\n\n\n3,4\n");
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void Headers_are_trimmed_and_lower_cased()
    {
        var rows = Csv.ParseObjects("  File_No , First_Name \nP-1,Ada");
        Assert.Equal("P-1", rows[0]["file_no"]);
        Assert.Equal("Ada", rows[0]["first_name"]);
    }

    [Fact]
    public void Numbers_tolerate_the_shapes_spreadsheets_produce()
    {
        Assert.Equal(1.25, Csv.Number("+1.25"));
        Assert.Equal(1200, Csv.Number("1,200"));
        Assert.Equal(8500, Csv.Number(" 8500 "));
        Assert.Null(Csv.Number(""));
        Assert.Null(Csv.Number(null));
        Assert.Throws<FormatException>(() => Csv.Number("eight thousand"));
    }

    [Fact]
    public void Writing_quotes_anything_a_spreadsheet_could_misread()
    {
        var csv = Csv.Write([new Dictionary<string, object?>
        {
            ["a"] = "one, two",
            ["b"] = "He said \"hi\"",
            ["c"] = "plain",
        }]);

        Assert.Contains("\"one, two\"", csv);
        Assert.Contains("\"He said \"\"hi\"\"\"", csv);
        Assert.Contains(",plain", csv);
        Assert.StartsWith("﻿", csv);
    }

    [Fact]
    public void A_written_file_parses_back_to_what_went_in()
    {
        // The round-trip property is the whole point of the export format.
        var original = new Dictionary<string, object?>
        {
            ["sku"] = "VC-RAVI-BLA",
            ["name"] = "Ravi, \"Matte\" Black",
            ["price"] = 6500m,
            ["notes"] = "line one\nline two",
        };

        var parsed = Csv.ParseObjects(Csv.Write([original]));

        Assert.Single(parsed);
        Assert.Equal("VC-RAVI-BLA", parsed[0]["sku"]);
        Assert.Equal("Ravi, \"Matte\" Black", parsed[0]["name"]);
        Assert.Equal("6500", parsed[0]["price"]);
        Assert.Equal("line one\nline two", parsed[0]["notes"]);
    }
}

[Collection("checkout")]
public class DataTransferTests(CheckoutFlowFixture fixture)
{
    // --- Export -------------------------------------------------------------

    [Fact]
    public async Task Every_dataset_exports_with_a_header_row()
    {
        using var scope = fixture.NewScope();
        var export = scope.ServiceProvider.GetRequiredService<IExportService>();

        foreach (var (key, _, _) in export.Datasets)
        {
            var file = await export.ExportAsync(key);

            Assert.EndsWith(".csv", file.Filename);
            Assert.StartsWith("﻿", file.Content);

            var rows = Csv.Parse(file.Content);
            Assert.NotEmpty(rows);
        }
    }

    [Fact]
    public async Task An_empty_dataset_still_exports_its_column_headers()
    {
        // A brand-new shop with no patients yet must still be able to export the
        // file, fill it in and import it back. Before the columns were declared,
        // an empty dataset produced a zero-byte file with no header at all, and
        // the round trip was impossible from a standing start.
        using var scope = fixture.NewScope();
        var export = scope.ServiceProvider.GetRequiredService<IExportService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var hasPrescriptions = await db.Prescriptions.AnyAsync();
        var file = await export.ExportAsync("prescriptions");
        var rows = Csv.Parse(file.Content);

        Assert.NotEmpty(rows);
        Assert.Contains("file_no", rows[0]);
        Assert.Contains("od_sphere", rows[0]);

        if (!hasPrescriptions) Assert.Single(rows);
    }

    [Theory]
    [InlineData("frames")]
    [InlineData("patients")]
    [InlineData("prescriptions")]
    [InlineData("orders")]
    public async Task The_declared_columns_match_what_the_export_actually_writes(string dataset)
    {
        // The column names are written twice — once in the declared list that
        // makes an empty export possible, once in the dictionary that builds each
        // row. This is the test that stops the two drifting apart.
        using var scope = fixture.NewScope();
        var export = scope.ServiceProvider.GetRequiredService<IExportService>();

        var rows = Csv.Parse((await export.ExportAsync(dataset)).Content);
        Assert.NotEmpty(rows);

        var header = rows[0];

        // Every header is unique, non-empty, and safe to use as a spreadsheet
        // column name — the importer matches on these exactly.
        Assert.Equal(header.Count, header.Distinct().Count());
        Assert.All(header, column => Assert.False(string.IsNullOrWhiteSpace(column)));
        Assert.All(header, column => Assert.Equal(column.Trim().ToLowerInvariant(), column));

        if (rows.Count > 1)
            Assert.All(rows.Skip(1), row =>
                Assert.Equal(header.Count, row.Count));
    }

    [Fact]
    public async Task The_frames_export_is_round_trippable()
    {
        using var scope = fixture.NewScope();
        var export = scope.ServiceProvider.GetRequiredService<IExportService>();

        var file = await export.ExportAsync("frames");
        var rows = Csv.ParseObjects(file.Content);

        Assert.NotEmpty(rows);

        // These are the columns the importer matches and requires; without them
        // an exported file cannot be edited and imported back.
        foreach (var required in new[] { "variant_sku", "frame_name", "price", "stock_qty" })
            Assert.True(rows[0].ContainsKey(required), $"export is missing {required}");

        // Price is emitted in major units, which is what a human edits.
        var price = Csv.Number(rows[0]["price"]);
        Assert.NotNull(price);
        Assert.True(price < 100_000, "price should be major units, not minor");
    }

    [Fact]
    public async Task Exporting_patient_data_is_itself_audited()
    {
        using var scope = fixture.NewScope();
        var export = scope.ServiceProvider.GetRequiredService<IExportService>();
        await export.ExportAsync("patients");

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logged = await db.AuditLogs.AsNoTracking()
            .AnyAsync(a => a.Action == Application.Platform.AuditActions.ExportPatients);

        // A file of clinical data leaving the building is an event worth recording.
        Assert.True(logged);
    }

    // --- Import -------------------------------------------------------------

    private const string FrameCsv =
        "variant_sku,frame_sku,frame_name,price,stock_qty,color_name,status\n" +
        "ZZ-TEST-BLA,ZZ-TEST,ZZ Import Frame,4250,7,Matte Black,active";

    [Fact]
    public async Task A_dry_run_reports_but_writes_nothing()
    {
        using var scope = fixture.NewScope();
        var import = scope.ServiceProvider.GetRequiredService<IImportService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var outcome = await import.ImportAsync("frames", "check.csv", FrameCsv, dryRun: true, null);

        Assert.False(outcome.IsFatal);
        Assert.Equal(1, outcome.Total);
        Assert.Equal(1, outcome.Ok);
        Assert.True(outcome.DryRun);

        // The whole point of the check: production data is untouched.
        Assert.False(await db.FrameVariants.AnyAsync(v => v.Sku == "ZZ-TEST-BLA"));
    }

    [Fact]
    public async Task A_real_import_creates_the_frame_and_converts_money_at_the_edge()
    {
        using var scope = fixture.NewScope();
        var import = scope.ServiceProvider.GetRequiredService<IImportService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var outcome = await import.ImportAsync("frames", "real.csv", FrameCsv, dryRun: false, null);
        Assert.Equal(1, outcome.Ok);

        var variant = await db.FrameVariants.AsNoTracking()
            .Include(v => v.Frame).FirstAsync(v => v.Sku == "ZZ-TEST-BLA");

        Assert.Equal(7, variant.StockQty);
        Assert.Equal("Matte Black", variant.ColorName);
        Assert.Equal(425000, variant.Frame.BasePriceMinor);   // 4250 major → 425000 minor
        Assert.Equal("ZZ Import Frame", variant.Frame.Name);

        await CleanFrameAsync(db);
    }

    [Fact]
    public async Task Re_importing_the_same_file_updates_rather_than_duplicating()
    {
        using var scope = fixture.NewScope();
        var import = scope.ServiceProvider.GetRequiredService<IImportService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await import.ImportAsync("frames", "first.csv", FrameCsv, dryRun: false, null);

        var edited = FrameCsv.Replace(",4250,7,", ",4995,3,");
        await import.ImportAsync("frames", "second.csv", edited, dryRun: false, null);

        // Matched on variant_sku — this is what makes an edited export safe to
        // send back rather than a way to double the catalogue.
        var matches = await db.FrameVariants.AsNoTracking()
            .Where(v => v.Sku == "ZZ-TEST-BLA").ToListAsync();

        Assert.Single(matches);
        Assert.Equal(3, matches[0].StockQty);

        var frame = await db.Frames.AsNoTracking().FirstAsync(f => f.Sku == "ZZ-TEST");
        Assert.Equal(499500, frame.BasePriceMinor);

        await CleanFrameAsync(db);
    }

    [Fact]
    public async Task A_bad_row_is_reported_by_line_number_and_the_rest_still_import()
    {
        var csv =
            "variant_sku,frame_name,price\n" +
            "ZZ-GOOD-1,Good One,1000\n" +
            ",Missing Sku,1000\n" +
            "ZZ-GOOD-2,Good Two,not-a-number\n" +
            "ZZ-GOOD-3,Good Three,2000";

        using var scope = fixture.NewScope();
        var import = scope.ServiceProvider.GetRequiredService<IImportService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var outcome = await import.ImportAsync("frames", "mixed.csv", csv, dryRun: false, null);

        Assert.Equal(4, outcome.Total);
        Assert.Equal(2, outcome.Ok);
        Assert.Equal(2, outcome.Failed);

        // Line numbers are what staff see in their spreadsheet: header is line 1.
        Assert.Contains(outcome.Errors, e => e.Row == 3 && e.Message.Contains("variant_sku"));
        Assert.Contains(outcome.Errors, e => e.Row == 4 && e.Message.Contains("not a number"));

        Assert.True(await db.FrameVariants.AnyAsync(v => v.Sku == "ZZ-GOOD-1"));
        Assert.True(await db.FrameVariants.AnyAsync(v => v.Sku == "ZZ-GOOD-3"));

        var strays = await db.Frames.Where(f => f.Name.StartsWith("Good ")).ToListAsync();
        db.Frames.RemoveRange(strays);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task An_imported_prescription_arrives_pending_verification()
    {
        var fileNo = $"P-9{Random.Shared.Next(10000, 99999)}";
        var csv =
            "file_no,first_name,last_name,od_sphere,od_cylinder,od_axis,os_sphere,pd_mm\n" +
            $"{fileNo},Imported,Patient,-2.25,-0.75,175,-2.50,63";

        using var scope = fixture.NewScope();
        var import = scope.ServiceProvider.GetRequiredService<IImportService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var outcome = await import.ImportAsync("patients", "rx.csv", csv, dryRun: false, null);
        Assert.Equal(1, outcome.Ok);

        var patient = await db.Patients.AsNoTracking()
            .Include(p => p.Prescriptions).FirstAsync(p => p.FileNo == fileNo);

        Assert.Equal(63, patient.PdMm);

        var rx = Assert.Single(patient.Prescriptions);
        // Never trusted straight to the lab, however it arrived.
        Assert.Equal(RxStatuses.PendingVerification, rx.Status);
        Assert.Equal(RxSources.Imported, rx.Source);
        Assert.Equal(-2.25, rx.OdSphere);
        Assert.Equal(175, rx.OdAxis);
    }

    [Fact]
    public async Task Import_enforces_the_same_clinical_rules_as_the_forms()
    {
        var csv =
            "file_no,first_name,od_sphere,od_cylinder,od_axis\n" +
            "P-90001,No Axis,-2.00,-0.75,\n" +      // cylinder without axis
            "P-90002,Off Step,-2.13,,";              // not a 0.25 D step

        using var scope = fixture.NewScope();
        var import = scope.ServiceProvider.GetRequiredService<IImportService>();

        var outcome = await import.ImportAsync("patients", "bad-rx.csv", csv, dryRun: true, null);

        Assert.Equal(2, outcome.Failed);
        Assert.Contains(outcome.Errors, e => e.Message.Contains("od_axis"));
        Assert.Contains(outcome.Errors, e => e.Message.Contains("0.25"));
    }

    [Fact]
    public async Task An_empty_or_headerless_file_is_refused_before_a_job_is_created()
    {
        using var scope = fixture.NewScope();
        var import = scope.ServiceProvider.GetRequiredService<IImportService>();

        var empty = await import.ImportAsync("frames", "empty.csv", "", dryRun: true, null);
        Assert.True(empty.IsFatal);

        var headerOnly = await import.ImportAsync("frames", "head.csv", "variant_sku,price", true, null);
        Assert.True(headerOnly.IsFatal);
    }

    [Fact]
    public async Task Every_import_is_recorded_as_a_job_with_its_row_counts()
    {
        using var scope = fixture.NewScope();
        var import = scope.ServiceProvider.GetRequiredService<IImportService>();

        await import.ImportAsync("frames", "history-check.csv", FrameCsv, dryRun: true, null);

        var jobs = await import.RecentJobsAsync();
        var job = jobs.First(j => j.Filename == "history-check.csv");

        Assert.Equal(1, job.TotalRows);
        Assert.Equal(1, job.OkRows);
        Assert.True(job.IsDryRun);
        Assert.Equal(ImportJobStatuses.Completed, job.Status);
        Assert.NotNull(job.FinishedAt);
    }

    private static async Task CleanFrameAsync(ApplicationDbContext db)
    {
        var frame = await db.Frames.FirstOrDefaultAsync(f => f.Sku == "ZZ-TEST");
        if (frame is null) return;
        db.Frames.Remove(frame);
        await db.SaveChangesAsync();
    }

    // --- Media --------------------------------------------------------------

    /// <summary>
    /// A real PNG, encoded here rather than pasted as a base64 constant — the
    /// image pipeline genuinely decodes it, so the test must supply something a
    /// decoder accepts.
    /// </summary>
    private static Stream PngStream(int width = 48, int height = 32)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(0x0b, 0x5f, 0xa5));
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new MemoryStream(data.ToArray());
    }

    [Fact]
    public async Task An_uploaded_image_is_processed_and_listed()
    {
        using var scope = fixture.NewScope();
        var media = scope.ServiceProvider.GetRequiredService<IMediaService>();

        var result = await media.UploadAsync(PngStream(), "zz-test-shot.png", "image/png",
            "zz-test", keepAlpha: false, userId: null);

        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.MediaId);

        var page = await media.ListAsync(new MediaFilters { Tag = "zz-test" });
        Assert.Contains(page.Items, m => m.Id == result.MediaId);

        var asset = page.Items.First(m => m.Id == result.MediaId);
        Assert.Equal("local", asset.StorageProvider);
        Assert.NotNull(asset.StorageKey);
        Assert.NotNull(asset.ThumbStorageKey);
        Assert.True(asset.SizeBytes > 0);

        await media.DeleteAsync(result.MediaId!);
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_reported_by_name_not_thrown()
    {
        using var scope = fixture.NewScope();
        var media = scope.ServiceProvider.GetRequiredService<IMediaService>();

        var result = await media.UploadAsync(
            new MemoryStream("not an image"u8.ToArray()),
            "notes.txt", "text/plain", null, false, null);

        // A corrupt file in a 60-photo shoot must be named, not take the batch down.
        Assert.False(result.Ok);
        Assert.Equal("notes.txt", result.Filename);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Deleting_an_image_removes_the_storage_object_not_just_the_row()
    {
        using var scope = fixture.NewScope();
        var media = scope.ServiceProvider.GetRequiredService<IMediaService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var uploaded = await media.UploadAsync(PngStream(), "zz-orphan.png", "image/png",
            "zz-test", false, null);
        Assert.True(uploaded.Ok, uploaded.Error);

        var result = await media.DeleteAsync(uploaded.MediaId!);
        Assert.True(result.Ok, result.Error);

        var asset = await db.MediaAssets.AsNoTracking().FirstAsync(m => m.Id == uploaded.MediaId);

        // The legacy implementation left the object in storage forever. Now the
        // row records that the object actually went.
        Assert.NotNull(asset.DeletedAt);
        Assert.NotNull(asset.PurgedAt);
        Assert.Null(asset.PurgeError);

        // And it drops out of the library.
        var page = await media.ListAsync(new MediaFilters { Tag = "zz-test" });
        Assert.DoesNotContain(page.Items, m => m.Id == uploaded.MediaId);
    }

    [Fact]
    public async Task An_image_in_use_on_a_colourway_cannot_be_deleted()
    {
        using var scope = fixture.NewScope();
        var media = scope.ServiceProvider.GetRequiredService<IMediaService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var uploaded = await media.UploadAsync(PngStream(), "zz-in-use.png", "image/png",
            "zz-test", false, null);

        var variantId = await db.FrameVariants.Select(v => v.Id).FirstAsync();
        var attached = await media.AttachToVariantAsync(
            uploaded.MediaId!, variantId, ProductImageRoles.Gallery);
        Assert.True(attached.Ok, attached.Error);

        // A product page with a dead image is worse than one extra library row.
        var deleted = await media.DeleteAsync(uploaded.MediaId!);
        Assert.False(deleted.Ok);
        Assert.Contains("attached", deleted.Error!, StringComparison.OrdinalIgnoreCase);

        // Clean up: detach, then delete.
        var asset = await db.MediaAssets.AsNoTracking().FirstAsync(m => m.Id == uploaded.MediaId);
        db.ProductImages.RemoveRange(db.ProductImages.Where(p => p.Url == asset.Url));
        await db.SaveChangesAsync();
        await media.DeleteAsync(uploaded.MediaId!);
    }

    [Fact]
    public async Task Attaching_try_on_artwork_makes_the_colourway_appear_in_the_mirror()
    {
        using var scope = fixture.NewScope();
        var media = scope.ServiceProvider.GetRequiredService<IMediaService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var uploaded = await media.UploadAsync(PngStream(), "zz-overlay.png", "image/png",
            "zz-test", keepAlpha: true, userId: null);

        var variant = await db.FrameVariants.FirstAsync();
        var before = variant.TryOnImageUrl;

        var attached = await media.AttachToVariantAsync(
            uploaded.MediaId!, variant.Id, ProductImageRoles.TryOn);
        Assert.True(attached.Ok, attached.Error);

        var after = await db.FrameVariants.AsNoTracking().FirstAsync(v => v.Id == variant.Id);
        Assert.NotEqual(before, after.TryOnImageUrl);
        Assert.Equal(uploaded.Url, after.TryOnImageUrl);

        // Transparency is preserved for overlays; everything else becomes WebP.
        Assert.EndsWith(".png", uploaded.Url);

        // Restore.
        var tracked = await db.FrameVariants.FirstAsync(v => v.Id == variant.Id);
        tracked.TryOnImageUrl = before;
        db.ProductImages.RemoveRange(db.ProductImages.Where(p => p.Url == uploaded.Url));
        await db.SaveChangesAsync();
        await media.DeleteAsync(uploaded.MediaId!);
    }
}
