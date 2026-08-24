using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Common;
using VisionCart.Application.DataTransfer;
using VisionCart.Application.Media;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;
using VisionCart.Web.Areas.Admin.Models;

namespace VisionCart.Web.Areas.Admin.Controllers;

/// <summary>
/// The media library: bulk upload, search, and attaching an image to a colourway.
///
/// Files are posted one at a time by the browser so a 60-photo shoot reports
/// progress per file and a single corrupt image is named without taking the
/// batch down — the legacy behaviour, and the one that matters to whoever is
/// uploading.
/// </summary>
[Route("admin/media")]
public class MediaController(
    IMediaService media,
    IApplicationDbContext db,
    UserManager<ApplicationUser> users) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? q, [FromQuery] string? tag,
        [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var filters = new MediaFilters { Q = q, Tag = tag, Page = page };

        return View(new MediaViewModel
        {
            Results = await media.ListAsync(filters, ct),
            Tags = await media.TagsAsync(ct),
            Filters = filters,
            PendingPurges = await db.MediaAssets
                .CountAsync(m => m.DeletedAt != null && m.PurgedAt == null, ct),
        });
    }

    /// <summary>
    /// Receives one file. Returns JSON so the uploader can show per-file progress
    /// and keep going after a failure.
    /// </summary>
    [HttpPost("upload")]
    [EnableRateLimiting("upload")]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? file, [FromForm] string? tags,
        [FromForm] bool keepAlpha, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { ok = false, error = "No file was included." });

        await using var stream = file.OpenReadStream();
        var result = await media.UploadAsync(stream, file.FileName, file.ContentType,
            tags, keepAlpha, users.GetUserId(User), ct);

        return result.Ok
            ? Json(new { ok = true, url = result.Url, id = result.MediaId, filename = result.Filename })
            : BadRequest(new { ok = false, error = result.Error, filename = result.Filename });
    }

    [HttpPost("{id}/attach")]
    public async Task<IActionResult> Attach(string id, string variantId, string role,
        string? returnUrl, CancellationToken ct)
    {
        var result = await media.AttachToVariantAsync(id, variantId, role, ct);

        if (result.Ok) TempData["AdminOk"] = "Image attached.";
        else TempData["AdminError"] = result.Error;

        return returnUrl is not null && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/delete")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct) =>
        Back(await media.DeleteAsync(id, ct), nameof(Index));

    /// <summary>Runs the orphan sweep by hand, rather than waiting for the hourly one.</summary>
    [HttpPost("purge")]
    public async Task<IActionResult> Purge(CancellationToken ct)
    {
        var purged = await media.PurgePendingAsync(ct: ct);
        TempData["AdminOk"] = purged == 0
            ? "Nothing was waiting to be purged."
            : $"Purged {purged} orphaned file{(purged == 1 ? "" : "s")} from storage.";
        return RedirectToAction(nameof(Index));
    }
}

/// <summary>
/// Spreadsheet import and export.
///
/// The import always runs a check first: nothing is written until staff press
/// "import for real", and the check reports bad rows by line number so a broken
/// file is fixed before it reaches the catalogue.
/// </summary>
[Route("admin/import")]
public class ImportController(
    IImportService import,
    IExportService export,
    UserManager<ApplicationUser> users) : AdminControllerBase
{
    private const int MaxUploadBytes = 5 * 1024 * 1024;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(new ImportViewModel
    {
        Datasets = export.Datasets,
        Kinds = import.Kinds,
        RecentJobs = await import.RecentJobsAsync(ct: ct),
        Outcome = TempData["ImportOutcome"] is string json
            ? System.Text.Json.JsonSerializer.Deserialize<ImportOutcome>(json)
            : null,
    });

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string type, CancellationToken ct)
    {
        try
        {
            var file = await export.ExportAsync(type, ct);
            // UTF-8 without a BOM from the encoder: Csv.Write already prepends one.
            return File(new UTF8Encoding(false).GetBytes(file.Content), "text/csv", file.Filename);
        }
        catch (ArgumentException)
        {
            TempData["AdminError"] = "Unknown export.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("run")]
    [EnableRateLimiting("upload")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Run(IFormFile? file, [FromForm] string kind,
        [FromForm] bool dryRun, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            TempData["AdminError"] = "Choose a CSV file first.";
            return RedirectToAction(nameof(Index));
        }

        if (file.Length > MaxUploadBytes)
        {
            TempData["AdminError"] = "That file is over 5 MB.";
            return RedirectToAction(nameof(Index));
        }

        string content;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
            content = await reader.ReadToEndAsync(ct);

        var outcome = await import.ImportAsync(
            kind, file.FileName, content, dryRun, users.GetUserId(User), ct);

        if (outcome.IsFatal)
        {
            TempData["AdminError"] = outcome.FatalError;
            return RedirectToAction(nameof(Index));
        }

        TempData["ImportOutcome"] = System.Text.Json.JsonSerializer.Serialize(outcome);
        TempData["AdminOk"] = outcome.DryRun
            ? $"Checked {outcome.Total} row{(outcome.Total == 1 ? "" : "s")}. Nothing has been written yet."
            : $"Imported {outcome.Ok} of {outcome.Total} row{(outcome.Total == 1 ? "" : "s")}.";

        return RedirectToAction(nameof(Index));
    }
}
