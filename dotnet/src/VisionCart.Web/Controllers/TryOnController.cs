using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Catalogue;
using VisionCart.Application.Common;
using VisionCart.Application.Patients;
using VisionCart.Application.Platform;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;
using VisionCart.Application.Storage;
using VisionCart.Infrastructure.Storage;
using VisionCart.Web.Models;

namespace VisionCart.Web.Controllers;

/// <summary>
/// The virtual try-on.
///
/// The page hands the browser a JSON block of frames and asset URLs; everything
/// after that happens client-side. The only server route here is the snapshot
/// save, and it only ever receives an image the customer explicitly chose to
/// keep — never the original photograph or a camera frame.
/// </summary>
public class TryOnController(
    ICatalogService catalog,
    ISettingsService settings,
    IApplicationDbContext db,
    IStorageProvider storage,
    IPatientService patients,
    IAuditService audit,
    ICurrentUser currentUser,
    IAntiforgery antiforgery,
    ILogger<TryOnController> logger) : Controller
{
    [HttpGet("/try-on")]
    public async Task<IActionResult> Index([FromQuery] string? variant, CancellationToken ct)
    {
        if (!await settings.GetBoolAsync(SettingKeys.TryOnEnabled, ct))
            return View("Disabled");

        var frames = await catalog.ListTryOnFramesAsync(120, ct);

        var storePhotos = await settings.GetBoolAsync(SettingKeys.TryOnStoreCustomerPhotos, ct);
        var cameraEnabled = await settings.GetBoolAsync(SettingKeys.TryOnCameraEnabled, ct);

        // A PD an optician has already recorded saves the customer typing it
        // again — and is a better number than anything a webcam will produce.
        double? knownPd = null;
        if (currentUser.IsAuthenticated)
        {
            knownPd = await db.Patients.AsNoTracking()
                .Where(p => p.UserId == currentUser.UserId)
                .Select(p => p.PdMm)
                .FirstOrDefaultAsync(ct);
        }

        return View(new TryOnViewModel
        {
            Frames = frames,
            InitialVariantId = variant,
            KnownPdMm = knownPd,
            // Saving needs BOTH a signed-in customer and the store setting on.
            CanSave = currentUser.IsAuthenticated && storePhotos,
            CameraEnabled = cameraEnabled,
            AntiforgeryToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken ?? string.Empty,
        });
    }

    /// <summary>
    /// Saves a try-on snapshot to the customer's file.
    ///
    /// This is the only path by which any image from the try-on reaches the
    /// server, and it only runs when the customer presses "Save to my file".
    /// </summary>
    [HttpPost("/api/tryon/snapshot")]
    [EnableRateLimiting("upload")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> Snapshot(
        IFormFile? image, [FromForm] string? variantId, [FromForm] string? source,
        [FromForm] double? pdMm, [FromForm] double? pdConfidence, [FromForm] string? faceShape,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Json401("Sign in to save snapshots to your file.");

        // Hiding the button is not enough — a store that has turned snapshot
        // retention off must not accept an image posted directly to this route.
        if (!await settings.GetBoolAsync(SettingKeys.TryOnStoreCustomerPhotos, ct))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "This store doesn't keep try-on photos. Use Download instead." });
        }

        if (image is null || image.Length == 0)
            return BadRequest(new { error = "No image was included." });

        var variant = await db.FrameVariants.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == variantId, ct);

        if (variant is null)
            return NotFound(new { error = "That frame no longer exists." });

        try
        {
            await using var stream = image.OpenReadStream();
            var stored = await storage.StoreImageAsync(
                stream, image.FileName, image.ContentType, "tryon", keepAlpha: false, ct);

            var patient = await patients.EnsureForUserAsync(currentUser.UserId!, ct);

            var session = new TryOnSession
            {
                UserId = currentUser.UserId,
                PatientId = patient.Id,
                Source = source == TryOnSources.Camera ? TryOnSources.Camera : TryOnSources.Upload,
                FaceData = JsonSerializer.Serialize(new
                {
                    pdMm, pdConfidence, faceShape, at = DateTime.UtcNow,
                }),
            };
            session.Snapshots.Add(new TryOnSnapshot
            {
                VariantId = variant.Id,
                ImageUrl = stored.Url,
            });

            db.TryOnSessions.Add(session);

            db.MediaAssets.Add(new MediaAsset
            {
                Url = stored.Url,
                ThumbUrl = stored.ThumbUrl,
                Filename = stored.Filename,
                MimeType = stored.MimeType,
                SizeBytes = stored.SizeBytes,
                Width = stored.Width,
                Height = stored.Height,
                StorageKey = stored.StorageKey,
                ThumbStorageKey = stored.ThumbStorageKey,
                StorageProvider = storage.Name,
                UploadedBy = currentUser.UserId,
                Tags = "tryon",
            });

            // A confident measurement fills a gap in the file; it never
            // overwrites a PD an optician has already recorded by hand.
            if (pdMm is { } pd && pdConfidence >= 0.5 && patient.PdMm is null)
            {
                var tracked = await db.Patients.FirstAsync(p => p.Id == patient.Id, ct);
                tracked.PdMm = pd;
                tracked.FaceMetrics = JsonSerializer.Serialize(new
                {
                    pdMm = pd, pdConfidence, faceShape, source = "tryon",
                });
            }

            await db.SaveChangesAsync(ct);

            await audit.WriteAsync(AuditActions.TryOnSnapshotSave, "Patient", patient.Id,
                new { variantId = variant.Id, pdMm, pdConfidence }, ct);

            return Json(new { ok = true, url = stored.Url });
        }
        catch (UploadException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Try-on snapshot save failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Could not save the snapshot." });
        }
    }

    private IActionResult Json401(string message) =>
        StatusCode(StatusCodes.Status401Unauthorized, new { error = message });
}
