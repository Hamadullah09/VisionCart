namespace VisionCart.Application.Storage;

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
