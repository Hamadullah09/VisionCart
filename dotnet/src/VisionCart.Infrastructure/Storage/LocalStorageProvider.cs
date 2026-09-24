using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using VisionCart.Application.Storage;

namespace VisionCart.Infrastructure.Storage;

/// <summary>
/// Writes into wwwroot and serves from there. Zero setup, which is what makes a
/// fresh deployment work before anyone signs up for object storage.
///
/// Replaces the legacy <c>sharp</c> pipeline with SkiaSharp: EXIF auto-rotation,
/// a 2000&#160;px cap on the longest edge, and a 400&#160;px thumbnail — the same
/// behaviour, and no commercially-licensed dependency.
/// </summary>
public sealed class LocalStorageProvider(
    IWebHostEnvironment environment,
    IOptions<StorageOptions> options,
    ILogger<LocalStorageProvider> logger) : IStorageProvider
{
    private const int MasterMaxEdge = 2000;
    private const int ThumbMaxEdge = 400;

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/avif",
    };

    private readonly StorageOptions _options = options.Value;

    public string Name => "local";

    public async Task<StoredImage> StoreImageAsync(
        Stream content, string originalName, string contentType, string folder,
        bool keepAlpha = false, bool removeBackground = false, CancellationToken ct = default)
    {
        // A cut-out without an alpha channel is just a picture of a frame, so
        // the two travel together rather than letting a caller ask for one and
        // silently lose it to WebP.
        if (removeBackground) keepAlpha = true;

        if (!Allowed.Contains(contentType))
            throw new UploadException($"{originalName} is not an image we can accept.");

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        if (buffer.Length > _options.MaxBytes)
        {
            throw new UploadException(
                $"{originalName} is {buffer.Length / 1024.0 / 1024.0:F1} MB — " +
                $"the limit is {_options.MaxBytes / 1024 / 1024} MB.");
        }

        buffer.Position = 0;

        // Decode with orientation applied. A phone photo carries its rotation in
        // EXIF; without this a portrait shot arrives on its side and every pupil
        // coordinate derived from it is wrong.
        using var codec = SKCodec.Create(buffer)
            ?? throw new UploadException($"{originalName} could not be read as an image.");

        using var decoded = SKBitmap.Decode(codec)
            ?? throw new UploadException($"{originalName} could not be decoded.");

        using var oriented = ApplyOrientation(decoded, codec.EncodedOrigin);

        var stem = SafeStem(originalName);
        var prefix = $"{folder}/{DateTime.UtcNow:yyyy/MM}";
        var unique = Guid.NewGuid().ToString("N")[..10];

        // Try-on overlays stay PNG so they keep their transparency; everything
        // else becomes WebP.
        var extension = keepAlpha ? "png" : "webp";
        var format = keepAlpha ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Webp;
        var mime = keepAlpha ? "image/png" : "image/webp";

        var key = $"{prefix}/{stem}-{unique}.{extension}";
        var thumbKey = $"{prefix}/{stem}-{unique}-thumb.{extension}";

        // Background removal runs on the master rather than the original: the
        // cap bounds the cost of the flood fill, and deriving the thumbnail
        // from the cut-out keeps the two consistent — a thumbnail still showing
        // the studio backdrop is how you end up with a library nobody trusts.
        ArtworkGeometry? geometry = null;

        using var master = removeBackground
            ? CutOut(Resize(oriented, MasterMaxEdge), originalName, out geometry)
            : Resize(oriented, MasterMaxEdge);

        using var thumb = Resize(master, ThumbMaxEdge);

        var masterBytes = Encode(master, format);
        var thumbBytes = Encode(thumb, format);

        await WriteAsync(key, masterBytes, ct);
        await WriteAsync(thumbKey, thumbBytes, ct);

        return new StoredImage
        {
            Url = PublicUrl(key),
            ThumbUrl = PublicUrl(thumbKey),
            StorageKey = key,
            ThumbStorageKey = thumbKey,
            Filename = Path.GetFileName(key),
            MimeType = mime,
            SizeBytes = masterBytes.Length,
            Width = master.Width,
            Height = master.Height,
            Geometry = geometry,
        };
    }

    public Task<bool> DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        try
        {
            var path = ResolveInsideRoot(storageKey);
            if (File.Exists(path)) File.Delete(path);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not delete stored file {Key}", storageKey);
            return Task.FromResult(false);
        }
    }

    private async Task WriteAsync(string key, byte[] data, CancellationToken ct)
    {
        var path = ResolveInsideRoot(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, data, ct);
    }

    /// <summary>
    /// Resolves a storage key to a path and proves it stays inside the upload
    /// root.
    ///
    /// SECURITY — this check did not exist in the legacy implementation, which
    /// joined a caller-supplied URL straight onto the web root and called
    /// <c>fs.rm</c> on the result. A key containing <c>../</c> could reach any
    /// file the process could write.
    /// </summary>
    private string ResolveInsideRoot(string key)
    {
        var root = Path.GetFullPath(Path.Combine(
            environment.WebRootPath, _options.LocalDirectory));

        var candidate = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));

        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(candidate, root, StringComparison.Ordinal))
        {
            throw new UploadException("Rejected a storage path that escapes the upload directory.");
        }

        return candidate;
    }

    private string PublicUrl(string key) => $"/{_options.LocalDirectory}/{key}";

    /// <summary>
    /// Removes the background, or explains why it would not.
    ///
    /// Takes ownership of <paramref name="resized"/> and disposes it, because
    /// the cut-out is a new bitmap and the caller only ever wants one of the two.
    ///
    /// The refusals matter as much as the success. Guessing here produces a
    /// frame with its temples erased, or a white rectangle over a customer's
    /// eyes, and either one is discovered by a shopper rather than by staff.
    /// </summary>
    private SKBitmap CutOut(
        SKBitmap resized, string originalName, out ArtworkGeometry? geometry)
    {
        geometry = null;

        try
        {
            var result = BackgroundRemover.Remove(resized);

            if (result.Succeeded)
            {
                // The lens openings and the frame front were measured in order
                // to do the cut. Handing them back saves marking the same points
                // by hand, and more importantly stops the mirror falling back to
                // proportions that belong to a different kind of picture.
                if (result.Openings.Count == 2)
                {
                    geometry = new ArtworkGeometry
                    {
                        LeftLensCenterX = result.Openings[0].CentreX,
                        LeftLensCenterY = result.Openings[0].CentreY,
                        RightLensCenterX = result.Openings[1].CentreX,
                        RightLensCenterY = result.Openings[1].CentreY,
                        FrontLeftX = result.FrontLeftX,
                        FrontRightX = result.FrontRightX,
                        LensTopY = Math.Min(result.Openings[0].TopY, result.Openings[1].TopY),
                        LensBottomY = Math.Max(result.Openings[0].BottomY, result.Openings[1].BottomY),
                    };
                }

                logger.LogInformation(
                    "Removed the background from {Name}: {Removed:P0} of the image, " +
                    "{Openings} enclosed opening(s), background sampled as #{Colour}",
                    originalName, result.RemovedFraction, result.OpeningsCleared,
                    result.SampledBackground.ToString());

                return result.Bitmap!;
            }

            throw new UploadException(result.Outcome switch
            {
                BackgroundRemovalOutcome.BackgroundNotPlain =>
                    $"{originalName} does not have a plain background — only " +
                    $"{result.BorderAgreement:P0} of its edge is one colour. Photograph the " +
                    "frame against a single plain backdrop, or upload artwork that is already " +
                    "cut out.",

                BackgroundRemovalOutcome.NotFrontOn =>
                    $"{originalName} was photographed at an angle. The background came away " +
                    "cleanly, but try-on artwork has to be front-on: the mirror lines the two " +
                    "lenses up with the customer's pupils, and in a three-quarter view one lens " +
                    "is foreshortened, so the frame sits skewed across the face. Re-shoot the " +
                    "frame square to the camera.",

                BackgroundRemovalOutcome.InsufficientContrast =>
                    $"{originalName} does not have enough contrast between the frame and its " +
                    "background to cut out safely — parts of the frame would be erased along " +
                    "with the backdrop. Shoot a pale frame against a darker backdrop, or a dark " +
                    "frame against a pale one.",

                BackgroundRemovalOutcome.SubjectTooSimilar =>
                    $"{originalName} is too close in colour to its background — removing it " +
                    $"would erase {result.RemovedFraction:P0} of the picture, frame included. " +
                    "Shoot a pale frame against a darker backdrop, or the other way round.",

                BackgroundRemovalOutcome.NothingToRemove =>
                    $"{originalName} has no plain background to remove. If it is already cut " +
                    "out, upload it without background removal.",

                _ => $"{originalName} could not have its background removed.",
            });
        }
        finally
        {
            resized.Dispose();
        }
    }

    /// <summary>Longest-edge resize, preserving aspect ratio. Never upscales.</summary>
    private static SKBitmap Resize(SKBitmap source, int maxEdge)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= maxEdge) return source.Copy();

        var scale = (double)maxEdge / longest;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        return source.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? source.Copy();
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 82);
        return data.ToArray();
    }

    /// <summary>Applies the EXIF orientation the decoder reported.</summary>
    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return source.Copy();

        var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        var width = swapsAxes ? source.Height : source.Width;
        var height = swapsAxes ? source.Width : source.Height;

        var rotated = new SKBitmap(width, height);
        using var canvas = new SKCanvas(rotated);

        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Scale(-1, 1, width / 2f, 0); break;
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180, width / 2f, height / 2f); break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Scale(1, -1, 0, height / 2f); break;
            case SKEncodedOrigin.LeftTop:
                canvas.Translate(width, 0); canvas.RotateDegrees(90); canvas.Scale(1, -1, 0, source.Height / 2f); break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(width, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(0, height); canvas.RotateDegrees(270); canvas.Scale(1, -1, 0, source.Height / 2f); break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, height); canvas.RotateDegrees(270); break;
        }

        canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKCubicResampler.Mitchell));
        return rotated;
    }

    private static string SafeStem(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
        var cleaned = new string([.. stem.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')])
            .Trim('-');

        while (cleaned.Contains("--", StringComparison.Ordinal))
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);

        return cleaned.Length == 0 ? "image" : cleaned[..Math.Min(60, cleaned.Length)];
    }
}
