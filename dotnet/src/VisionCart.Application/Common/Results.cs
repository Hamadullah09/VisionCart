namespace VisionCart.Application.Common;

/// <summary>
/// The legacy server actions returned <c>{ ok: true } | { ok: false, error }</c>
/// and, for forms, a field-error map. These reproduce that shape so controllers
/// can translate straight into model state without inventing a second vocabulary.
/// </summary>
public readonly record struct ActionResult(bool Ok, string? Error = null)
{
    public static ActionResult Success() => new(true);
    public static ActionResult Fail(string error) => new(false, error);
}

public readonly record struct ActionResult<T>(bool Ok, T? Value, string? Error = null)
{
    public static ActionResult<T> Success(T value) => new(true, value);
    public static ActionResult<T> Fail(string error) => new(false, default, error);
}

/// <summary>Form submission outcome carrying per-field messages.</summary>
public sealed class FormResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public Dictionary<string, string> FieldErrors { get; init; } = [];
    public string? RedirectTo { get; init; }

    public static FormResult Success(string? redirectTo = null) => new() { Ok = true, RedirectTo = redirectTo };

    public static FormResult Fail(string error, Dictionary<string, string>? fieldErrors = null) =>
        new() { Ok = false, Error = error, FieldErrors = fieldErrors ?? [] };
}

/// <summary>A page of results. Used everywhere a table could grow unbounded.</summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; } = 1;
    public int PerPage { get; init; } = 24;
    public int Pages => Math.Max(1, (int)Math.Ceiling((double)Total / Math.Max(1, PerPage)));
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < Pages;
}
