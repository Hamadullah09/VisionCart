namespace VisionCart.Domain.Constants;

/// <summary>
/// Port of the original <c>src/lib/constants.ts</c>.
///
/// The schema stores these as plain strings so it stays portable across SQL
/// Server, MySQL and SQLite. This file is the single source of truth for what
/// those strings may be — validation, drop-downs and labels all read from here.
/// Deliberately not C# enums: the database column stays <c>nvarchar</c> so a new
/// value never requires a schema migration, exactly as in the original design.
/// </summary>
public static class Roles
{
    public const string Customer = "customer";
    public const string Staff = "staff";
    public const string Optician = "optician";
    public const string Admin = "admin";

    public static readonly IReadOnlyList<string> All = [Customer, Staff, Optician, Admin];

    /// <summary>Roles allowed into /admin, weakest first.</summary>
    public static readonly IReadOnlyList<string> StaffRoles = [Staff, Optician, Admin];

    public static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>
        {
            [Customer] = "Customer",
            [Staff] = "Staff",
            [Optician] = "Optician",
            [Admin] = "Administrator",
        };
}

public static class AuthorizationPolicies
{
    public const string StaffOnly = "StaffOnly";
    public const string OpticianOnly = "OpticianOnly";
    public const string AdminOnly = "AdminOnly";
}

public static class FrameShapes
{
    public static readonly IReadOnlyList<string> All =
    [
        "rectangle", "square", "round", "oval", "aviator",
        "cat_eye", "wayfarer", "geometric", "browline",
    ];
}

public static class FrameMaterials
{
    public static readonly IReadOnlyList<string> All =
        ["acetate", "metal", "titanium", "tr90", "stainless", "mixed", "wood"];
}

public static class RimTypes
{
    public const string FullRim = "full_rim";
    public static readonly IReadOnlyList<string> All = [FullRim, "semi_rimless", "rimless"];
}

/// <summary>
/// Fitting features a dispenser reaches for when a frame nearly fits.
///
/// Constrained strings rather than an enum, so the schema stays portable and a
/// new feature is a data change rather than a migration.
/// </summary>
public static class FrameFeatures
{
    public const string AdjustableNosePads = "adjustable_nose_pads";
    public const string IntegratedNosePads = "integrated_nose_pads";
    public const string LowBridgeFit = "low_bridge_fit";
    public const string SpringHinges = "spring_hinges";
    public const string Flexible = "flexible";
    public const string Lightweight = "lightweight";
    public const string Hypoallergenic = "hypoallergenic";
    public const string AdjustableTemples = "adjustable_temples";

    public static readonly IReadOnlyList<string> All =
    [
        AdjustableNosePads, IntegratedNosePads, LowBridgeFit, SpringHinges,
        Flexible, Lightweight, Hypoallergenic, AdjustableTemples,
    ];

    /// <summary>What each one means to somebody choosing a frame.</summary>
    public static readonly IReadOnlyDictionary<string, string> Help =
        new Dictionary<string, string>
        {
            [AdjustableNosePads] = "Metal pads a dispenser can bend to sit right on your nose.",
            [IntegratedNosePads] = "Moulded into the frame — nothing to adjust, nothing to snap off.",
            [LowBridgeFit] = "Built for a lower nose bridge, so the frame stops sliding down.",
            [SpringHinges] = "Arms that flex outward, so the frame survives being taken off one-handed.",
            [Flexible] = "Bends a long way and comes back. Good with children.",
            [Lightweight] = "Under about 20 grams — you stop noticing it.",
            [Hypoallergenic] = "No nickel. For skin that reacts to ordinary metal frames.",
            [AdjustableTemples] = "Arms that can be shortened or curved to the ear.",
        };
}

/// <summary>
/// How a practice grades its stock for promotion: A moves, C does not.
/// </summary>
public static class PromotionGrades
{
    public static readonly IReadOnlyList<string> All = ["A", "B", "C"];
}

public static class Genders
{
    public const string Unisex = "unisex";
    public static readonly IReadOnlyList<string> All = ["men", "women", Unisex, "kids"];
}

public static class FaceShapes
{
    public static readonly IReadOnlyList<string> All =
        ["oval", "round", "square", "heart", "diamond", "oblong"];
}

public static class SizeBands
{
    public const string Narrow = "narrow";
    public const string Medium = "medium";
    public const string Wide = "wide";
    public static readonly IReadOnlyList<string> All = [Narrow, Medium, Wide];
}

public static class ProductStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Archived = "archived";
    public static readonly IReadOnlyList<string> All = [Draft, Active, Archived];
}

public static class OrderStatuses
{
    public const string Pending = "pending";
    public const string Paid = "paid";
    public const string InLab = "in_lab";
    public const string Ready = "ready";
    public const string Shipped = "shipped";
    public const string Delivered = "delivered";
    public const string Cancelled = "cancelled";
    public const string Refunded = "refunded";

    public static readonly IReadOnlyList<string> All =
        [Pending, Paid, InLab, Ready, Shipped, Delivered, Cancelled, Refunded];
}

public static class PaymentStatuses
{
    public const string Unpaid = "unpaid";
    public const string Authorized = "authorized";
    public const string Paid = "paid";
    public const string PartiallyRefunded = "partially_refunded";
    public const string Refunded = "refunded";
    public const string Failed = "failed";

    public static readonly IReadOnlyList<string> All =
        [Unpaid, Authorized, Paid, PartiallyRefunded, Refunded, Failed];
}

public static class FulfilmentStatuses
{
    public const string Unfulfilled = "unfulfilled";
    public static readonly IReadOnlyList<string> All =
        [Unfulfilled, "lab_processing", "quality_check", "packed", "shipped", "delivered"];
}

public static class LabStatuses
{
    public const string Pending = "pending";
    public const string Ready = "ready";
    public static readonly IReadOnlyList<string> All =
        [Pending, "ordered", "surfacing", "coating", "glazing", "qc", Ready];
}

public static class RxStatuses
{
    public const string Draft = "draft";
    public const string PendingVerification = "pending_verification";
    public const string Verified = "verified";
    public const string Rejected = "rejected";
    public const string Expired = "expired";

    public static readonly IReadOnlyList<string> All =
        [Draft, PendingVerification, Verified, Rejected, Expired];
}

public static class RxSources
{
    public const string ManualEntry = "manual_entry";
    public const string Uploaded = "uploaded";
    public const string InStoreExam = "in_store_exam";
    public const string Imported = "imported";

    public static readonly IReadOnlyList<string> All = [ManualEntry, Uploaded, InStoreExam, Imported];
}

/// <summary>Lens builder steps, rendered in this order on the product page.</summary>
public static class LensGroups
{
    public const string Usage = "usage";
    public const string Type = "type";
    public const string Index = "index";
    public const string Coating = "coating";
    public const string Tint = "tint";
    public const string Extra = "extra";

    public static readonly IReadOnlyList<string> All = [Usage, Type, Index, Coating, Tint, Extra];

    /// <summary>Groups that only make sense with a real prescription.</summary>
    public static readonly IReadOnlySet<string> RxOnly = new HashSet<string> { Index, Type };

    public static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>
        {
            [Usage] = "What are these glasses for?",
            [Type] = "Lens type",
            [Index] = "Lens thickness",
            [Coating] = "Coatings & protection",
            [Tint] = "Tint",
            [Extra] = "Extras",
        };

    /// <summary>
    /// Position of a group in the wizard. Used to render the six steps in the
    /// order a customer walks them, rather than the alphabetical order the
    /// database returns. An unknown group sorts last rather than throwing.
    /// </summary>
    public static int OrderOf(string group)
    {
        for (var i = 0; i < All.Count; i++)
            if (All[i] == group) return i;
        return int.MaxValue;
    }

    public static readonly IReadOnlyDictionary<string, string> Help =
        new Dictionary<string, string>
        {
            [Usage] = "Tell us how you'll wear them so we fit the right lens.",
            [Type] = "Single vision covers one distance. Progressives blend near and far.",
            [Index] = "Stronger prescriptions look and feel better in a thinner lens.",
            [Coating] = "Anti-reflective and hard coat are recommended on every lens.",
            [Tint] = "Optional colour for sunglasses or light sensitivity.",
            [Extra] = "Finishing touches.",
        };
}

public static class PromotionKinds
{
    public const string PercentOff = "percent_off";
    public const string AmountOff = "amount_off";
    public const string FreeShipping = "free_shipping";
    public const string Bogo = "bogo";
    public const string FreeLensUpgrade = "free_lens_upgrade";
    public const string Bundle = "bundle";

    public static readonly IReadOnlyList<string> All =
        [PercentOff, AmountOff, FreeShipping, Bogo, FreeLensUpgrade, Bundle];

    public static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>
        {
            [PercentOff] = "Percentage off",
            [AmountOff] = "Fixed amount off",
            [FreeShipping] = "Free shipping",
            [Bogo] = "Buy one get one",
            [FreeLensUpgrade] = "Free lens upgrade",
            [Bundle] = "Bundle price",
        };
}

public static class PaymentProviders
{
    public const string Stripe = "stripe";
    public const string Cod = "cod";
    public const string BankTransfer = "bank_transfer";
    public const string Manual = "manual";

    public static readonly IReadOnlyList<string> All = [Stripe, Cod, BankTransfer];
}

public static class Carriers
{
    public static readonly IReadOnlyList<string> All =
        ["tcs", "leopards", "dhl", "fedex", "ups", "local", "other"];
}

public static class AppointmentKinds
{
    public const string EyeTest = "eye_test";
    public static readonly IReadOnlyList<string> All =
        [EyeTest, "fitting", "collection", "adjustment", "follow_up"];
}

public static class AppointmentStatuses
{
    public const string Scheduled = "scheduled";
    public const string Completed = "completed";
    public const string NoShow = "no_show";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Scheduled, Completed, NoShow, Cancelled];
}

/// <summary>
/// Kinds of data-subject request. The schema stays portable — no enums — so the
/// permitted values live here and are validated on the way in.
/// </summary>
public static class DataSubjectRequestKinds
{
    public const string Correction = "correction";
    public const string Erasure = "erasure";
    public const string Export = "export";
    public const string Restriction = "restriction";

    public static readonly IReadOnlyList<string> All = [Correction, Erasure, Export, Restriction];

    public static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>
        {
            [Correction] = "Correct my details",
            [Erasure] = "Erase my data",
            [Export] = "Send me a copy of my data",
            [Restriction] = "Restrict how my data is used",
        };
}

public static class DataSubjectRequestStatuses
{
    public const string Pending = "pending";
    public const string InReview = "in_review";
    public const string Completed = "completed";
    public const string Rejected = "rejected";

    public static readonly IReadOnlyList<string> All = [Pending, InReview, Completed, Rejected];

    /// <summary>A request in one of these states still needs someone to act.</summary>
    public static readonly IReadOnlyList<string> Open = [Pending, InReview];
}

public static class PatientDocumentKinds
{
    public const string Other = "other";
    public static readonly IReadOnlyList<string> All =
        ["prescription_scan", "id_document", "insurance", "photo", Other];
}

public static class ProductImageRoles
{
    public const string Gallery = "gallery";
    public const string Primary = "primary";
    public const string TryOn = "try_on";
    public static readonly IReadOnlyList<string> All =
        [Gallery, Primary, TryOn, "swatch", "lifestyle", "360"];
}

public static class TryOnSources
{
    public const string Upload = "upload";
    public const string Camera = "camera";
    public static readonly IReadOnlyList<string> All = [Upload, Camera];
}

public static class ImportJobStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";

    public static readonly IReadOnlyList<string> All = [Pending, Running, Completed, Failed];
}

public static class ShipmentStatuses
{
    public const string Pending = "pending";
    public static readonly IReadOnlyList<string> All =
        [Pending, "label_created", "in_transit", "out_for_delivery", "delivered", "returned"];
}

/// <summary>Prism base directions used on the Rx form.</summary>
public static class PrismBases
{
    public static readonly IReadOnlyList<string> All = ["in", "out", "up", "down"];
}

public static class Diopters
{
    /// <summary>
    /// Sphere/cylinder/add steps in 0.25 D — used to build the Rx dropdowns so a
    /// customer can never key in an unfillable value like -2.13.
    /// </summary>
    public static IReadOnlyList<decimal> Range(decimal min, decimal max, decimal step = 0.25m)
    {
        var output = new List<decimal>();
        for (var v = min; v <= max; v += step) output.Add(v);
        return output;
    }

    public static readonly IReadOnlyList<decimal> SphereValues = Range(-20m, 20m);
    public static readonly IReadOnlyList<decimal> CylinderValues = Range(-6m, 6m);
    public static readonly IReadOnlyList<decimal> AddValues = Range(0.75m, 3.5m);
    public static readonly IReadOnlyList<int> AxisValues = [.. Enumerable.Range(1, 180)];

    /// <summary>True when a value sits on an exact 0.25 D step.</summary>
    public static bool IsQuarterStep(double value) =>
        Math.Abs(value * 4 - Math.Round(value * 4)) < 1e-6;

    /// <summary>Signed diopter display: -2.25 stays, 1.5 becomes +1.50.</summary>
    public static string Format(double? v)
    {
        if (v is null) return "—";
        var sign = v > 0 ? "+" : v < 0 ? "-" : "";
        return $"{sign}{Math.Abs(v.Value):F2}";
    }
}

public static class Humanise
{
    /// <summary>Turn <c>cat_eye</c> into <c>Cat eye</c> for any of the constant lists above.</summary>
    public static string Value(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var s = value.Replace('_', ' ');
        return char.ToUpperInvariant(s[0]) + s[1..];
    }
}
