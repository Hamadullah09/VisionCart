using VisionCart.Application.Appointments;
using VisionCart.Application.Carts;
using VisionCart.Application.Catalogue;
using VisionCart.Application.Checkout;
using VisionCart.Application.Common;
using VisionCart.Application.Payments;
using VisionCart.Application.Prescriptions;
using VisionCart.Application.Privacy;
using VisionCart.Application.Shipping;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;

namespace VisionCart.Web.Models;

public sealed class HomeViewModel
{
    public IReadOnlyList<FrameCard> Featured { get; init; } = [];
    public IReadOnlyList<Promotion> Banners { get; init; } = [];
    public IReadOnlyList<Promotion> Deals { get; init; } = [];
}

public sealed class CatalogueViewModel
{
    public PagedResult<FrameCard> Results { get; init; } = new();
    public CatalogueFacets Facets { get; init; } = new();
    public FrameFilters Filters { get; init; } = new();
}

public sealed class ProductViewModel
{
    public Frame Frame { get; init; } = null!;
    public IReadOnlyList<LensOption> LensOptions { get; init; } = [];

    /// <summary>
    /// Grouped in the canonical wizard order, not alphabetically. The database
    /// returns options ordered by group name, which would render the six steps
    /// as Coatings, Extras, Thickness, Tint, Type, Usage — a nonsensical sequence
    /// for a customer. VisionCart.Domain.Constants.LensGroups.All is the order
    /// the storefront must present.
    /// </summary>
    public IEnumerable<IGrouping<string, LensOption>> LensGroups =>
        LensOptions
            .GroupBy(o => o.Group)
            .OrderBy(g => Domain.Constants.LensGroups.OrderOf(g.Key));
}

public sealed class CheckoutViewModel
{
    public CartView Cart { get; init; } = new();
    public CheckoutInput Input { get; init; } = new();
    public IReadOnlyList<PaymentMethodMeta> PaymentMethods { get; init; } = [];
    public IReadOnlyList<ShippingQuote> ShippingQuotes { get; init; } = [];
    public bool GuestAllowed { get; init; }
}

public sealed class TryOnViewModel
{
    public IReadOnlyList<TryOnFrame> Frames { get; init; } = [];
    public string? InitialVariantId { get; init; }

    /// <summary>
    /// A PD already recorded on this customer's file, so they need not type it
    /// twice. Only ever pre-fills the field — it is still theirs to change.
    /// </summary>
    public double? KnownPdMm { get; init; }

    public bool CanSave { get; init; }
    public bool CameraEnabled { get; init; }
    public string AntiforgeryToken { get; init; } = string.Empty;
}

public sealed class ErrorViewModel
{
    public int StatusCode { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// The product page's add-to-bag form. Diopter fields arrive as strings because
/// they come from drop-downs whose empty option means "not given"; parsing here
/// keeps the nullable semantics the prescription model expects.
/// </summary>
public sealed class AddToCartForm
{
    public string VariantId { get; set; } = string.Empty;
    public int Qty { get; set; } = 1;
    public List<string>? LensOptionCodes { get; set; }
    public string? ReturnUrl { get; set; }

    /// <summary>prescription | plain | frame_only</summary>
    public string LensMode { get; set; } = "frame_only";

    public double? OdSphere { get; set; }
    public double? OdCylinder { get; set; }
    public int? OdAxis { get; set; }
    public double? OdAdd { get; set; }
    public double? OsSphere { get; set; }
    public double? OsCylinder { get; set; }
    public int? OsAxis { get; set; }
    public double? OsAdd { get; set; }
    public double? PdMm { get; set; }

    public PrescriptionInput? ToPrescriptionInput()
    {
        if (LensMode != "prescription") return null;

        return new PrescriptionInput
        {
            Od = new EyeRx { Sphere = OdSphere, Cylinder = OdCylinder, Axis = OdAxis, Add = OdAdd },
            Os = new EyeRx { Sphere = OsSphere, Cylinder = OsCylinder, Axis = OsAxis, Add = OsAdd },
            PdMm = PdMm,
        };
    }
}

// --- Customer account area --------------------------------------------------

public sealed class AppointmentsViewModel
{
    public string PatientId { get; init; } = string.Empty;
    public IReadOnlyList<Appointment> Appointments { get; init; } = [];
}

public sealed class BookAppointmentViewModel
{
    public DateOnly Date { get; init; }
    public string Kind { get; init; } = AppointmentKinds.EyeTest;
    public IReadOnlyList<SlotOption> Slots { get; init; } = [];
    public DateOnly LastBookableDate { get; init; }
}
