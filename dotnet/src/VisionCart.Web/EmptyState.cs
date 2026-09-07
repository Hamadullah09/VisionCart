namespace VisionCart.Web;

/// <summary>
/// What a listing shows when it has nothing to list.
///
/// These were one-line info alerts — "No deals configured yet." — which is
/// accurate and useless: it tells somebody the page is empty, which they can
/// already see, and not what to do about it. An empty listing is the one
/// moment a screen has the operator's whole attention, so it carries the
/// action that fills it.
///
/// <paramref name="Body"/> should say why the list is empty and what changes
/// it; <see cref="ActionText"/> is the thing that does.
/// </summary>
public sealed record EmptyState(string Icon, string Title, string Body)
{
    /// <summary>Primary call to action. Both parts must be set to render.</summary>
    public string? ActionText { get; init; }
    public string? ActionUrl { get; init; }

    /// <summary>Usually "clear the filters", when a search caused the emptiness.</summary>
    public string? SecondaryText { get; init; }
    public string? SecondaryUrl { get; init; }
}
