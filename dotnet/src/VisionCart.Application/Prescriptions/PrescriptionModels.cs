using System.Text.Json.Serialization;
using VisionCart.Domain.Constants;

namespace VisionCart.Application.Prescriptions;

/// <summary>
/// Port of <c>src/lib/rx.ts</c>. Kept separate from pricing because the same
/// shape is used by the customer wizard, the optician's back-office form and
/// CSV import.
/// </summary>
public sealed class EyeRx
{
    public double? Sphere { get; set; }
    public double? Cylinder { get; set; }
    public int? Axis { get; set; }
    public double? Add { get; set; }
    public double? Prism { get; set; }
    public string? PrismBase { get; set; }
    public double? PdMm { get; set; }
    public double? SegHeightMm { get; set; }
}

public sealed class PrescriptionInput
{
    public EyeRx Od { get; set; } = new();
    public EyeRx Os { get; set; } = new();
    public double? PdMm { get; set; }
    public double? PdNearMm { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Prescriber { get; set; }
    public string? Clinic { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// The flattened columns the database stores, and the shape a cart line's
/// prescription draft is serialised as. Property names match the legacy JSON so
/// an in-flight draft written by the old application still deserialises.
/// </summary>
public sealed class FlatRx
{
    [JsonPropertyName("odSphere")] public double? OdSphere { get; set; }
    [JsonPropertyName("odCylinder")] public double? OdCylinder { get; set; }
    [JsonPropertyName("odAxis")] public int? OdAxis { get; set; }
    [JsonPropertyName("odAdd")] public double? OdAdd { get; set; }
    [JsonPropertyName("odPrism")] public double? OdPrism { get; set; }
    [JsonPropertyName("odPrismBase")] public string? OdPrismBase { get; set; }
    [JsonPropertyName("odPdMm")] public double? OdPdMm { get; set; }
    [JsonPropertyName("odSegHeightMm")] public double? OdSegHeightMm { get; set; }

    [JsonPropertyName("osSphere")] public double? OsSphere { get; set; }
    [JsonPropertyName("osCylinder")] public double? OsCylinder { get; set; }
    [JsonPropertyName("osAxis")] public int? OsAxis { get; set; }
    [JsonPropertyName("osAdd")] public double? OsAdd { get; set; }
    [JsonPropertyName("osPrism")] public double? OsPrism { get; set; }
    [JsonPropertyName("osPrismBase")] public string? OsPrismBase { get; set; }
    [JsonPropertyName("osPdMm")] public double? OsPdMm { get; set; }
    [JsonPropertyName("osSegHeightMm")] public double? OsSegHeightMm { get; set; }

    [JsonPropertyName("pdMm")] public double? PdMm { get; set; }
    [JsonPropertyName("pdNearMm")] public double? PdNearMm { get; set; }
    [JsonPropertyName("prescriber")] public string? Prescriber { get; set; }
    [JsonPropertyName("clinic")] public string? Clinic { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
}

public sealed record RxValidationError(string Path, string Message);

public sealed class RxValidationResult
{
    public List<RxValidationError> Errors { get; } = [];
    public bool IsValid => Errors.Count == 0;

    public void Add(string path, string message) => Errors.Add(new RxValidationError(path, message));

    public Dictionary<string, string> ToFieldErrors()
    {
        var map = new Dictionary<string, string>();
        foreach (var e in Errors) map.TryAdd(e.Path, e.Message);
        return map;
    }
}

public static class Rx
{
    // Ranges taken verbatim from the legacy Zod schema. Anything outside them is
    // a custom lab order and is refused with the same wording the customer saw.
    private const double SphereMin = -20, SphereMax = 20;
    private const double CylinderMin = -6, CylinderMax = 6;
    private const double AddMin = 0.75, AddMax = 3.5;
    private const double PrismMax = 10;
    private const double MonoPdMin = 20, MonoPdMax = 40;
    private const double BinocularPdMin = 40, BinocularPdMax = 80;
    private const double SegHeightMin = 10, SegHeightMax = 40;

    public static RxValidationResult Validate(PrescriptionInput rx)
    {
        var result = new RxValidationResult();

        foreach (var (side, eye) in new[] { ("od", rx.Od), ("os", rx.Os) })
        {
            ValidateEye(result, side, eye);

            // A cylinder value is meaningless without the axis it sits on, and
            // vice versa. Catching it here saves a remake at the lab.
            var hasCyl = eye.Cylinder is not null and not 0;
            var hasAxis = eye.Axis is not null;

            if (hasCyl && !hasAxis)
                result.Add($"{side}.axis", "Axis is required when a cylinder value is given.");
            if (hasAxis && !hasCyl)
                result.Add($"{side}.cylinder", "Cylinder is required when an axis is given.");
            if (eye.Prism is not null and not 0 && string.IsNullOrWhiteSpace(eye.PrismBase))
                result.Add($"{side}.prismBase", "Prism base direction is required.");
        }

        var bothEmpty = rx.Od.Sphere is null && rx.Os.Sphere is null
                     && rx.Od.Cylinder is null && rx.Os.Cylinder is null;
        if (bothEmpty)
        {
            result.Add("od.sphere",
                "Enter at least one eye's prescription, or choose non-prescription lenses.");
        }

        var hasMonoPd = rx.Od.PdMm is not null || rx.Os.PdMm is not null;
        if (!hasMonoPd && rx.PdMm is null)
        {
            result.Add("pdMm",
                "Pupillary distance is required — measure it with the try-on tool if you don't know it.");
        }

        if (rx.PdMm is { } pd && (pd < BinocularPdMin || pd > BinocularPdMax))
            result.Add("pdMm", $"Pupillary distance must be between {BinocularPdMin} and {BinocularPdMax} mm.");

        if (rx.PdNearMm is { } pdn && (pdn < BinocularPdMin || pdn > BinocularPdMax))
            result.Add("pdNearMm", $"Near pupillary distance must be between {BinocularPdMin} and {BinocularPdMax} mm.");

        return result;
    }

    private static void ValidateEye(RxValidationResult result, string side, EyeRx eye)
    {
        if (eye.Sphere is { } sph)
        {
            if (sph < SphereMin)
                result.Add($"{side}.sphere", $"Sphere below {SphereMin:F2} needs a custom lab order — please call us.");
            else if (sph > SphereMax)
                result.Add($"{side}.sphere", $"Sphere above +{SphereMax:F2} needs a custom lab order — please call us.");
            else if (!Diopters.IsQuarterStep(sph))
                result.Add($"{side}.sphere", "Sphere must be in steps of 0.25.");
        }

        if (eye.Cylinder is { } cyl)
        {
            if (cyl < CylinderMin)
                result.Add($"{side}.cylinder", $"Cylinder below {CylinderMin:F2} needs a custom lab order — please call us.");
            else if (cyl > CylinderMax)
                result.Add($"{side}.cylinder", $"Cylinder above +{CylinderMax:F2} needs a custom lab order — please call us.");
            else if (!Diopters.IsQuarterStep(cyl))
                result.Add($"{side}.cylinder", "Cylinder must be in steps of 0.25.");
        }

        if (eye.Axis is { } axis && axis is < 0 or > 180)
            result.Add($"{side}.axis", "Axis must be between 0 and 180 degrees.");

        if (eye.Add is { } add)
        {
            if (add < AddMin || add > AddMax)
                result.Add($"{side}.add", $"Reading addition must be between +{AddMin:F2} and +{AddMax:F2}.");
            else if (!Diopters.IsQuarterStep(add))
                result.Add($"{side}.add", "Add must be in steps of 0.25.");
        }

        if (eye.Prism is { } prism && (prism < 0 || prism > PrismMax))
            result.Add($"{side}.prism", $"Prism must be between 0 and {PrismMax}.");

        if (!string.IsNullOrWhiteSpace(eye.PrismBase) && !PrismBases.All.Contains(eye.PrismBase))
            result.Add($"{side}.prismBase", "Prism base must be in, out, up or down.");

        if (eye.PdMm is { } mono && (mono < MonoPdMin || mono > MonoPdMax))
            result.Add($"{side}.pdMm", $"Monocular PD must be between {MonoPdMin} and {MonoPdMax} mm.");

        if (eye.SegHeightMm is { } seg && (seg < SegHeightMin || seg > SegHeightMax))
            result.Add($"{side}.segHeightMm", $"Segment height must be between {SegHeightMin} and {SegHeightMax} mm.");
    }

    /// <summary>Flatten the nested wizard shape into the columns the database stores.</summary>
    public static FlatRx ToFlat(PrescriptionInput rx) => new()
    {
        OdSphere = rx.Od.Sphere,
        OdCylinder = rx.Od.Cylinder,
        OdAxis = rx.Od.Axis,
        OdAdd = rx.Od.Add,
        OdPrism = rx.Od.Prism,
        OdPrismBase = rx.Od.PrismBase,
        OdPdMm = rx.Od.PdMm,
        OdSegHeightMm = rx.Od.SegHeightMm,
        OsSphere = rx.Os.Sphere,
        OsCylinder = rx.Os.Cylinder,
        OsAxis = rx.Os.Axis,
        OsAdd = rx.Os.Add,
        OsPrism = rx.Os.Prism,
        OsPrismBase = rx.Os.PrismBase,
        OsPdMm = rx.Os.PdMm,
        OsSegHeightMm = rx.Os.SegHeightMm,
        PdMm = rx.PdMm,
        PdNearMm = rx.PdNearMm,
        Prescriber = rx.Prescriber,
        Clinic = rx.Clinic,
        Notes = rx.Notes,
    };

    /// <summary>
    /// Strongest absolute sphere across both eyes — this is what decides whether
    /// a thin-index lens is required and whether a frame is a sensible choice.
    /// </summary>
    public static double StrongestSphere(FlatRx rx) =>
        Math.Max(Math.Abs(rx.OdSphere ?? 0), Math.Abs(rx.OsSphere ?? 0));

    public static double StrongestCylinder(FlatRx rx) =>
        Math.Max(Math.Abs(rx.OdCylinder ?? 0), Math.Abs(rx.OsCylinder ?? 0));

    /// <summary>True when either eye carries a reading addition (progressive/bifocal).</summary>
    public static bool NeedsAddition(FlatRx rx) => (rx.OdAdd ?? 0) != 0 || (rx.OsAdd ?? 0) != 0;

    /// <summary>
    /// The thinnest lens the customer can get away with. Used to pre-select the
    /// index step and to warn when the chosen one will look bottle-thick.
    /// </summary>
    public static string RecommendedIndex(FlatRx rx) => StrongestSphere(rx) switch
    {
        >= 8 => "1.74",
        >= 5.5 => "1.67",
        >= 3 => "1.61",
        _ => "1.50",
    };

    /// <summary>One-line summary for order lines, invoices and lab tickets.</summary>
    public static string Summarise(FlatRx rx)
    {
        static string Eye(double? s, double? c, int? a, double? add)
        {
            if (s is null && c is null) return "—";
            var parts = new List<string> { Diopters.Format(s ?? 0) };
            if (c is not null and not 0) parts.Add($"{Diopters.Format(c)} x {(a?.ToString() ?? "?")}°");
            if (add is not null and not 0) parts.Add($"Add {Diopters.Format(add)}");
            return string.Join(" ", parts);
        }

        return $"OD {Eye(rx.OdSphere, rx.OdCylinder, rx.OdAxis, rx.OdAdd)} | " +
               $"OS {Eye(rx.OsSphere, rx.OsCylinder, rx.OsAxis, rx.OsAdd)}";
    }

    public static FlatRx FromEntity(Domain.Entities.Prescription p) => new()
    {
        OdSphere = p.OdSphere, OdCylinder = p.OdCylinder, OdAxis = p.OdAxis, OdAdd = p.OdAdd,
        OdPrism = p.OdPrism, OdPrismBase = p.OdPrismBase, OdPdMm = p.OdPdMm, OdSegHeightMm = p.OdSegHeightMm,
        OsSphere = p.OsSphere, OsCylinder = p.OsCylinder, OsAxis = p.OsAxis, OsAdd = p.OsAdd,
        OsPrism = p.OsPrism, OsPrismBase = p.OsPrismBase, OsPdMm = p.OsPdMm, OsSegHeightMm = p.OsSegHeightMm,
        Prescriber = p.Prescriber, Clinic = p.Clinic, Notes = p.Notes,
    };
}
