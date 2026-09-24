namespace VisionCart.Application.Media;

public sealed class ArtworkRetouchReport
{
    public int Examined { get; init; }
    public int Repaired { get; init; }
    public int AlreadyClean { get; init; }
    public int Skipped { get; init; }

    /// <summary>One line per colourway that could not be done, and why.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];
}

/// <summary>
/// Repairs try-on artwork already in the catalogue.
///
/// Fixing the uploader only ever helps the next photograph. Everything that
/// went in before keeps whatever the uploader did on the day, and nobody
/// re-uploads a frame that looked fine at the time — so the arms stay lying
/// across customers' eyes, and the only symptom is that the mirror looks wrong.
/// </summary>
public interface IArtworkRetouchService
{
    Task<ArtworkRetouchReport> RetouchAllAsync(CancellationToken ct = default);
}
