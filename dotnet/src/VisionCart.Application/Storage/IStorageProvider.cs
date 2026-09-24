namespace VisionCart.Application.Storage;

/// <summary>
/// Where the frame sits inside a piece of try-on artwork, normalised to the
/// image. Measured while the background was being removed — the lens openings
/// are found in order to clear them, and the frame front in order to tell a
/// front-on photograph from a three-quarter one — so it costs nothing extra and
/// spares whoever uploaded the picture from marking the same points by hand.
/// </summary>
public sealed class ArtworkGeometry
{
    public required double LeftLensCenterX { get; init; }
    public required double LeftLensCenterY { get; init; }
    public required double RightLensCenterX { get; init; }
    public required double RightLensCenterY { get; init; }
    public required double FrontLeftX { get; init; }
    public required double FrontRightX { get; init; }
    public required double LensTopY { get; init; }
    public required double LensBottomY { get; init; }

    /// <summary>
    /// The cut-out's own dimensions. Recorded alongside the anchors because the
    /// readiness check reads them, and artwork replaced without them left the
    /// previous picture's size on the colourway.
    /// </summary>
    public required int ImageWidth { get; init; }
    public required int ImageHeight { get; init; }
}

public sealed class StoredImage
{
    public string Url { get; init; } = string.Empty;
    public string ThumbUrl { get; init; } = string.Empty;
    public string StorageKey { get; init; } = string.Empty;
    public string ThumbStorageKey { get; init; } = string.Empty;
    public string Filename { get; init; } = string.Empty;
    public string MimeType { get; init; } = string.Empty;
    public int SizeBytes { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>Set only when the background was removed and two lenses were found.</summary>
    public ArtworkGeometry? Geometry { get; init; }
}

public sealed class UploadException(string message) : Exception(message);

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    /// <summary>local | s3</summary>
    public string Provider { get; set; } = "local";
    public string LocalDirectory { get; set; } = "uploads";
    public int MaxBytes { get; set; } = 15 * 1024 * 1024;
}

public interface IStorageProvider
{
    string Name { get; }
    /// <param name="removeBackground">
    /// Cut the frame out of a plain background before storing, so a photograph
    /// can be used as try-on artwork. Implies <paramref name="keepAlpha"/>,
    /// because the result is meaningless without an alpha channel. Throws
    /// <see cref="UploadException"/> with an explanation if the image does not
    /// have a plain background to remove.
    /// </param>
    Task<StoredImage> StoreImageAsync(Stream content, string originalName, string contentType,
        string folder, bool keepAlpha = false, bool removeBackground = false,
        CancellationToken ct = default);
    Task<bool> DeleteAsync(string storageKey, CancellationToken ct = default);
}
