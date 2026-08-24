using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VisionCart.Application.Common;
using VisionCart.Application.Platform;
using VisionCart.Application.Storage;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;

namespace VisionCart.Application.Media;

public sealed class MediaFilters
{
    public string? Q { get; init; }
    public string? Tag { get; init; }
    public int Page { get; init; } = 1;
    public int PerPage { get; init; } = 40;
}

public sealed class UploadResult
{
    public string Filename { get; init; } = string.Empty;
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public string? Url { get; init; }
    public string? MediaId { get; init; }
}

public interface IMediaService
{
    Task<PagedResult<MediaAsset>> ListAsync(MediaFilters filters, CancellationToken ct = default);
    Task<IReadOnlyList<string>> TagsAsync(CancellationToken ct = default);

    Task<UploadResult> UploadAsync(Stream content, string filename, string contentType,
        string? tags, bool keepAlpha, string? userId, CancellationToken ct = default);

    Task<ActionResult> AttachToVariantAsync(string mediaId, string variantId, string role,
        CancellationToken ct = default);

    Task<ActionResult> DeleteAsync(string mediaId, CancellationToken ct = default);

    /// <summary>Retries objects whose storage delete failed. See the class remarks.</summary>
    Task<int> PurgePendingAsync(int batchSize = 50, CancellationToken ct = default);
}

/// <summary>
/// The media library, and the fix for the cloud-orphan defect.
///
/// The legacy implementation deleted the database row and, when the shop was on
/// object storage, deliberately left the file in the bucket — so storage grew
/// without bound and nothing recorded what had been abandoned.
///
/// Deletion is now two-phase. The row is marked <c>DeletedAt</c> and the storage
/// object is removed immediately; if that removal fails the row stays visible to
/// <see cref="PurgePendingAsync"/> with the error recorded, and is retried until
/// it succeeds. A file can no longer become an orphan silently — at worst it
/// becomes a pending purge somebody can see.
/// </summary>
public sealed class MediaService(
    IApplicationDbContext db,
    IStorageProvider storage,
    IAuditService audit,
    TimeProvider clock,
    ILogger<MediaService> logger) : IMediaService
{
    /// <summary>Stop retrying after this many failures; the row stays for a human.</summary>
    private const int MaxPurgeAttempts = 5;

    public async Task<PagedResult<MediaAsset>> ListAsync(
        MediaFilters filters, CancellationToken ct = default)
    {
        var query = db.MediaAssets.AsNoTracking().Where(m => m.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(filters.Q))
        {
            var term = filters.Q.Trim();
            query = query.Where(m =>
                EF.Functions.Like(m.Filename, $"%{term}%")
                || (m.Tags != null && EF.Functions.Like(m.Tags, $"%{term}%")));
        }

        if (!string.IsNullOrWhiteSpace(filters.Tag))
            query = query.Where(m => m.Tags != null && EF.Functions.Like(m.Tags, $"%{filters.Tag}%"));

        var total = await query.CountAsync(ct);
        var perPage = Math.Clamp(filters.PerPage, 1, 100);
        var page = Math.Max(1, filters.Page);

        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToListAsync(ct);

        return new PagedResult<MediaAsset> { Items = items, Total = total, Page = page, PerPage = perPage };
    }

    public async Task<IReadOnlyList<string>> TagsAsync(CancellationToken ct = default)
    {
        var raw = await db.MediaAssets.AsNoTracking()
            .Where(m => m.DeletedAt == null && m.Tags != null)
            .Select(m => m.Tags!)
            .ToListAsync(ct);

        return
        [
            .. raw.SelectMany(t => t.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .OrderBy(t => t),
        ];
    }

    /// <summary>
    /// Stores one file. Returns a result rather than throwing, so a corrupt file
    /// in a bulk shoot is reported by name and the rest still go through — the
    /// legacy behaviour, and the one that matters to someone uploading 60 photos.
    /// </summary>
    public async Task<UploadResult> UploadAsync(
        Stream content, string filename, string contentType, string? tags, bool keepAlpha,
        string? userId, CancellationToken ct = default)
    {
        try
        {
            var stored = await storage.StoreImageAsync(content, filename, contentType,
                "media", keepAlpha, ct);

            var asset = new MediaAsset
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
                UploadedBy = userId,
                Tags = string.IsNullOrWhiteSpace(tags) ? null : tags.Trim(),
            };

            db.MediaAssets.Add(asset);
            await db.SaveChangesAsync(ct);

            return new UploadResult
            {
                Filename = filename, Ok = true, Url = stored.Url, MediaId = asset.Id,
            };
        }
        catch (UploadException ex)
        {
            return new UploadResult { Filename = filename, Ok = false, Error = ex.Message };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Upload failed for {Filename}", filename);
            return new UploadResult
            {
                Filename = filename, Ok = false, Error = "That file could not be processed.",
            };
        }
    }

    public async Task<ActionResult> AttachToVariantAsync(
        string mediaId, string variantId, string role, CancellationToken ct = default)
    {
        if (!ProductImageRoles.All.Contains(role)) role = ProductImageRoles.Gallery;

        var asset = await db.MediaAssets.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == mediaId && m.DeletedAt == null, ct);

        if (asset is null) return ActionResult.Fail("That image is no longer in the library.");

        var variant = await db.FrameVariants.FirstOrDefaultAsync(v => v.Id == variantId, ct);
        if (variant is null) return ActionResult.Fail("That colourway no longer exists.");

        if (await db.ProductImages.AnyAsync(p => p.VariantId == variantId && p.Url == asset.Url, ct))
            return ActionResult.Fail("That image is already attached to this colourway.");

        var nextPosition = await db.ProductImages
            .Where(p => p.VariantId == variantId)
            .Select(p => (int?)p.Position)
            .MaxAsync(ct) ?? -1;

        db.ProductImages.Add(new ProductImage
        {
            VariantId = variantId,
            Url = asset.Url,
            ThumbUrl = asset.ThumbUrl,
            Alt = $"{variant.ColorName}",
            Role = role,
            Width = asset.Width,
            Height = asset.Height,
            Position = nextPosition + 1,
        });

        // Attaching try-on artwork is what makes a colourway appear in the mirror.
        if (role == ProductImageRoles.TryOn) variant.TryOnImageUrl = asset.Url;

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync("media.attach", "FrameVariant", variantId,
            new { mediaId, role }, ct);

        return ActionResult.Success();
    }

    public async Task<ActionResult> DeleteAsync(string mediaId, CancellationToken ct = default)
    {
        var asset = await db.MediaAssets.FirstOrDefaultAsync(m => m.Id == mediaId, ct);
        if (asset is null) return ActionResult.Fail("That image is no longer in the library.");

        // Refuse while it is still in use: a product page with a dead image is
        // worse than a library with one extra row in it.
        var inUse = await db.ProductImages.CountAsync(p => p.Url == asset.Url, ct);
        if (inUse > 0)
        {
            return ActionResult.Fail(
                $"That image is attached to {inUse} colourway{(inUse == 1 ? "" : "s")}. " +
                "Detach it there first.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        asset.DeletedAt = now;

        // Mark first, then remove the object. If the process dies between the two,
        // the purge sweep finds the row and finishes the job.
        await db.SaveChangesAsync(ct);
        await TryPurgeAsync(asset, now, ct);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync("media.delete", "MediaAsset", asset.Id,
            new { filename = asset.Filename, purged = asset.PurgedAt is not null }, ct);

        return ActionResult.Success();
    }

    /// <summary>
    /// Retries storage deletions that failed. Called by a hosted sweep, and safe
    /// to call at any time — it only touches rows already marked deleted.
    /// </summary>
    public async Task<int> PurgePendingAsync(int batchSize = 50, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var pending = await db.MediaAssets
            .Where(m => m.DeletedAt != null
                        && m.PurgedAt == null
                        && m.PurgeAttempts < MaxPurgeAttempts)
            .OrderBy(m => m.DeletedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        var purged = 0;
        foreach (var asset in pending)
        {
            if (await TryPurgeAsync(asset, now, ct)) purged++;
        }

        await db.SaveChangesAsync(ct);

        if (purged > 0)
            logger.LogInformation("Purged {Count} orphaned media object(s) from storage", purged);

        return purged;
    }

    private async Task<bool> TryPurgeAsync(MediaAsset asset, DateTime now, CancellationToken ct)
    {
        asset.PurgeAttempts++;

        try
        {
            var master = asset.StorageKey is null || await storage.DeleteAsync(asset.StorageKey, ct);
            var thumb = asset.ThumbStorageKey is null || await storage.DeleteAsync(asset.ThumbStorageKey, ct);

            if (master && thumb)
            {
                asset.PurgedAt = now;
                asset.PurgeError = null;
                return true;
            }

            asset.PurgeError = "The storage provider did not confirm the delete.";
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not purge media object {Key}", asset.StorageKey);
            asset.PurgeError = ex.Message.Length > 512 ? ex.Message[..512] : ex.Message;
            return false;
        }
    }
}
