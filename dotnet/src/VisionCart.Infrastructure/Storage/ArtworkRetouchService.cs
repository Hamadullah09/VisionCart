using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using VisionCart.Application.Media;
using VisionCart.Application.Common;
using VisionCart.Application.Storage;

namespace VisionCart.Infrastructure.Storage;

/// <summary>
/// Runs <see cref="BackgroundRemover.Retouch"/> over every piece of try-on
/// artwork the shop stores for itself.
///
/// The repaired cut-out is written beside the original rather than over it. The
/// uploaded file is also the colourway's gallery photograph, and a product shot
/// is allowed to show the whole pair — it is only the mirror that has nowhere
/// to put an arm. So the gallery keeps its picture and the mirror gets its own.
///
/// Safe to run repeatedly. Artwork with nothing left to take out is recognised
/// and left alone, so a second pass writes nothing.
/// </summary>
public sealed class ArtworkRetouchService(
    IApplicationDbContext db,
    IWebHostEnvironment environment,
    IOptions<StorageOptions> options,
    ILogger<ArtworkRetouchService> logger) : IArtworkRetouchService
{
    private const string Marker = "-tryon";

    private readonly StorageOptions _options = options.Value;

    public async Task<ArtworkRetouchReport> RetouchAllAsync(CancellationToken ct = default)
    {
        var prefix = $"/{_options.LocalDirectory}/";

        var variants = await db.FrameVariants
            .Where(v => v.TryOnImageUrl != null)
            .Select(v => new { v.Id, v.ColorName, v.TryOnImageUrl })
            .ToListAsync(ct);

        var repaired = 0;
        var clean = 0;
        var skipped = 0;
        var problems = new List<string>();

        foreach (var variant in variants)
        {
            var url = variant.TryOnImageUrl!;

            // Generated catalogue artwork lives outside the upload folder and is
            // not ours to rewrite. It has no arms behind its lenses either — it
            // was drawn, not photographed — and the mirror crops the ones off
            // its sides as it paints.
            if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            var path = ResolvePath(url);
            if (path is null || !File.Exists(path))
            {
                skipped++;
                problems.Add($"{variant.ColorName}: the artwork file is missing.");
                continue;
            }

            try
            {
                using var source = SKBitmap.Decode(path);
                if (source is null)
                {
                    skipped++;
                    problems.Add($"{variant.ColorName}: the artwork could not be read.");
                    continue;
                }

                var result = BackgroundRemover.Retouch(source);
                var removed = result.TempleIntrusionPixels + result.TempleSidePixels;

                if (result.Bitmap is null || removed == 0)
                {
                    result.Bitmap?.Dispose();
                    clean++;
                    continue;
                }

                using (result.Bitmap)
                {
                    var repairedPath = RepairedPath(path);
                    await WritePngAsync(result.Bitmap, repairedPath, ct);

                    var row = await db.FrameVariants.FirstAsync(v => v.Id == variant.Id, ct);
                    row.TryOnImageUrl = RepairedUrl(url);
                }

                repaired++;

                // The canvas is untouched, so the recorded dimensions and the
                // frame front still describe the picture. Only the arms are
                // gone, and they were never inside the front.
                logger.LogInformation(
                    "Retouched try-on artwork for {Colour}: {Behind} px behind the lenses, "
                    + "{Sides} px off the sides",
                    variant.ColorName, result.TempleIntrusionPixels, result.TempleSidePixels);
            }
            catch (Exception ex)
            {
                skipped++;
                problems.Add($"{variant.ColorName}: {ex.Message}");
                logger.LogWarning(ex, "Could not retouch artwork for {Colour}", variant.ColorName);
            }
        }

        if (repaired > 0) await db.SaveChangesAsync(ct);

        return new ArtworkRetouchReport
        {
            Examined = variants.Count,
            Repaired = repaired,
            AlreadyClean = clean,
            Skipped = skipped,
            Problems = problems,
        };
    }

    private string? ResolvePath(string url)
    {
        var relative = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(environment.WebRootPath);
        var full = Path.GetFullPath(Path.Combine(root, relative));

        // A url is data, and this one becomes a file path. Refuse anything that
        // climbs out of the web root.
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static string RepairedPath(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.EndsWith(Marker, StringComparison.Ordinal)) return path;
        return Path.Combine(directory, name + Marker + ".png");
    }

    private static string RepairedUrl(string url)
    {
        var dot = url.LastIndexOf('.');
        var stem = dot < 0 ? url : url[..dot];
        if (stem.EndsWith(Marker, StringComparison.Ordinal)) return stem + ".png";
        return stem + Marker + ".png";
    }

    private static async Task WritePngAsync(SKBitmap bitmap, string path, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        await using var file = File.Create(path);
        data.SaveTo(file);
        await file.FlushAsync(ct);
    }
}
