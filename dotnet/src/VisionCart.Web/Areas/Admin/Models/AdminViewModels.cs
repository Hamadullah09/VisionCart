using VisionCart.Application.Admin;
using VisionCart.Application.DataTransfer;
using VisionCart.Application.Media;
using VisionCart.Application.Common;
using VisionCart.Application.Prescriptions;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;
using VisionCart.Domain.ValueObjects;
using VisionCart.Application.Appointments;
using VisionCart.Application.Privacy;

namespace VisionCart.Web.Areas.Admin.Models;

public sealed class OrderListViewModel
{
    public PagedResult<OrderRow> Results { get; init; } = new();
    public int PaidTotalMinor { get; init; }
    public OrderFilters Filters { get; init; } = new();
}

public sealed class PatientListViewModel
{
    public PagedResult<PatientRow> Results { get; init; } = new();
    public PatientFilters Filters { get; init; } = new();
}

public sealed class PatientEditViewModel
{
    public PatientDetails Details { get; set; } = new();
}

public sealed class FrameListViewModel
{
    public PagedResult<Frame> Results { get; init; } = new();
    public string? Q { get; init; }
    public string? Status { get; init; }
}

public sealed class FrameEditViewModel
{
    public Frame? Frame { get; init; }
    public FrameDetails Details { get; set; } = new();
    public IReadOnlyList<Brand> Brands { get; init; } = [];
    public IReadOnlyList<Category> Categories { get; init; } = [];
    public IReadOnlyList<string> SelectedCategoryIds { get; init; } = [];

    /// <summary>
    /// Rebuilds the form model from a saved frame. Money comes back out of minor
    /// units here — the only place in the write path that converts in this
    /// direction, so staff see the price they typed.
    /// </summary>
    public static FrameDetails From(Frame f) => new()
    {
        Name = f.Name,
        Sku = f.Sku,
        Slug = f.Slug,
        BrandId = f.BrandId,
        Status = f.Status,
        Position = f.Position,
        Description = f.Description,
        Shape = f.Shape,
        Material = f.Material,
        RimType = f.RimType,
        Gender = f.Gender,
        FaceShapes = f.FaceShapes,
        LensWidthMm = f.LensWidthMm,
        BridgeWidthMm = f.BridgeWidthMm,
        TempleLengthMm = f.TempleLengthMm,
        LensHeightMm = f.LensHeightMm,
        TotalWidthMm = f.TotalWidthMm,
        WeightGrams = f.WeightGrams,
        Price = Money.FromMinor(f.BasePriceMinor, "PKR"),
        CompareAt = f.CompareAtMinor is { } c ? Money.FromMinor(c, "PKR") : null,
        Cost = f.CostMinor is { } cost ? Money.FromMinor(cost, "PKR") : null,
        AllowFrameOnly = f.AllowFrameOnly,
        RequiresPrescription = f.RequiresPrescription,
        IsFeatured = f.IsFeatured,
        MetaTitle = f.MetaTitle,
        MetaDesc = f.MetaDesc,
    };
}

public sealed class PromotionEditViewModel
{
    public string? Id { get; init; }
    public PromotionDetails Details { get; set; } = new();
    public int UsageCount { get; init; }

    public static PromotionDetails From(Promotion p) => new()
    {
        Name = p.Name,
        Description = p.Description,
        Code = p.Code,
        Kind = p.Kind,
        // Percent kinds hold basis points; money kinds hold minor units.
        Value = p.Kind == PromotionKinds.PercentOff
            ? p.Value / 100m
            : Money.FromMinor(p.Value, "PKR"),
        MaxDiscount = p.MaxDiscountMinor is { } m ? Money.FromMinor(m, "PKR") : null,
        MinSubtotal = Money.FromMinor(p.MinSubtotalMinor, "PKR"),
        MinQty = p.MinQty,
        BrandIds = p.BrandIds,
        CategoryIds = p.CategoryIds,
        FirstOrderOnly = p.FirstOrderOnly,
        StartsAt = p.StartsAt,
        EndsAt = p.EndsAt,
        UsageLimit = p.UsageLimit,
        UsageLimitPerUser = p.UsageLimitPerUser,
        Stackable = p.Stackable,
        Priority = p.Priority,
        IsActive = p.IsActive,
        BannerText = p.BannerText,
    };
}

public sealed class MediaViewModel
{
    public PagedResult<MediaAsset> Results { get; init; } = new();
    public IReadOnlyList<string> Tags { get; init; } = [];
    public MediaFilters Filters { get; init; } = new();

    /// <summary>Files marked deleted whose storage object has not gone yet.</summary>
    public int PendingPurges { get; init; }
}

public sealed class ImportViewModel
{
    public IReadOnlyList<(string Key, string Label, string Description)> Datasets { get; init; } = [];
    public IReadOnlyList<(string Key, string Label)> Kinds { get; init; } = [];
    public IReadOnlyList<ImportJob> RecentJobs { get; init; } = [];

    /// <summary>The result of the check or import that was just run, if any.</summary>
    public ImportOutcome? Outcome { get; init; }
}

public sealed class AuditViewModel
{
    public PagedResult<AuditRow> Results { get; init; } = new();
    public IReadOnlyList<string> Actions { get; init; } = [];
    public IReadOnlyList<string> Entities { get; init; } = [];
    public AuditFilters Filters { get; init; } = new();
}

public sealed class SettingsViewModel
{
    public IReadOnlyDictionary<string, string> Values { get; init; } =
        new Dictionary<string, string>();

    public string PaymentProviders { get; init; } = string.Empty;
    public bool StripeConfigured { get; init; }
    public string ShippingProvider { get; init; } = string.Empty;
    public string EmailDriver { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public int TaxRateBps { get; init; }
    public bool TaxInclusive { get; init; }

    public string Get(string key) => Values.TryGetValue(key, out var v) ? v : string.Empty;
    public bool Bool(string key) => Get(key) == "true";
}

/// <summary>
/// The optician's prescription form. Diopter fields arrive as nullable doubles
/// from drop-downs whose empty option means "not given".
/// </summary>
public sealed class PrescriptionForm
{
    public string Source { get; set; } = RxSources.InStoreExam;

    public double? OdSphere { get; set; }
    public double? OdCylinder { get; set; }
    public int? OdAxis { get; set; }
    public double? OdAdd { get; set; }
    public double? OdPrism { get; set; }
    public string? OdPrismBase { get; set; }
    public double? OdPdMm { get; set; }
    public double? OdSegHeightMm { get; set; }

    public double? OsSphere { get; set; }
    public double? OsCylinder { get; set; }
    public int? OsAxis { get; set; }
    public double? OsAdd { get; set; }
    public double? OsPrism { get; set; }
    public string? OsPrismBase { get; set; }
    public double? OsPdMm { get; set; }
    public double? OsSegHeightMm { get; set; }

    public double? PdMm { get; set; }
    public double? PdNearMm { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Prescriber { get; set; }
    public string? Clinic { get; set; }
    public string? Notes { get; set; }

    public PrescriptionInput ToInput() => new()
    {
        Od = new EyeRx
        {
            Sphere = OdSphere, Cylinder = OdCylinder, Axis = OdAxis, Add = OdAdd,
            Prism = OdPrism, PrismBase = OdPrismBase, PdMm = OdPdMm, SegHeightMm = OdSegHeightMm,
        },
        Os = new EyeRx
        {
            Sphere = OsSphere, Cylinder = OsCylinder, Axis = OsAxis, Add = OsAdd,
            Prism = OsPrism, PrismBase = OsPrismBase, PdMm = OsPdMm, SegHeightMm = OsSegHeightMm,
        },
        PdMm = PdMm,
        PdNearMm = PdNearMm,
        IssuedAt = IssuedAt,
        ExpiresAt = ExpiresAt,
        Prescriber = Prescriber,
        Clinic = Clinic,
        Notes = Notes,
    };
}

public sealed class DiaryViewModel
{
    public DateOnly From { get; init; }
    public int DayCount { get; init; }
    public IReadOnlyList<DiaryDay> Days { get; init; } = [];
    public IReadOnlyList<ApplicationUser> Clinicians { get; init; } = [];
}

public sealed class DataRequestsViewModel
{
    public PagedResult<DataSubjectRequest> Requests { get; init; } = new();
    public string? Status { get; init; }
}

public sealed class DataRequestDetailViewModel
{
    public DataSubjectRequest Request { get; init; } = null!;
    public ErasureImpact? Impact { get; init; }
    public Patient? Patient { get; init; }
}
