using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Admin;
using VisionCart.Application.Catalogue;
using VisionCart.Application.Common;
// MVC ships its own ActionResult; alias ours so the intent is unambiguous.
using DomainResult = VisionCart.Application.Common.ActionResult;
using VisionCart.Application.Platform;
using VisionCart.Application.Prescriptions;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;
using VisionCart.Web.Areas.Admin.Models;

namespace VisionCart.Web.Areas.Admin.Controllers;

/// <summary>
/// Base for every back-office controller.
///
/// The authorization policy is applied here rather than on each controller, so a
/// new screen cannot be added without it — the legacy application repeated a
/// <c>staff()</c> call in twenty places and relied on nobody forgetting.
/// </summary>
[Area("Admin")]
[Authorize(Policy = AuthorizationPolicies.StaffOnly)]
[Route("admin")]
public abstract class AdminControllerBase : Controller
{
    protected IActionResult Back(DomainResult result, string fallbackAction, object? routeValues = null)
    {
        if (result.Ok) TempData["AdminOk"] = "Saved.";
        else TempData["AdminError"] = result.Error;
        return RedirectToAction(fallbackAction, routeValues);
    }
}

public class DashboardController(IDashboardService dashboard) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await dashboard.BuildAsync(ct));
}

[Route("admin/orders")]
public class OrdersController(IOrderAdminService orders) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? q, [FromQuery] string? status, [FromQuery] string? paymentStatus,
        [FromQuery] string? labStatus, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var filters = new OrderFilters
        {
            Q = q, Status = status, PaymentStatus = paymentStatus, LabStatus = labStatus, Page = page,
        };

        return View(new OrderListViewModel
        {
            Results = await orders.ListAsync(filters, ct),
            PaidTotalMinor = await orders.PaidTotalAsync(filters, ct),
            Filters = filters,
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Detail(string id, CancellationToken ct)
    {
        var order = await orders.GetAsync(id, ct);
        return order is null ? NotFound() : View(order);
    }

    [HttpPost("{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, string? status, string? fulfilmentStatus,
        string? internalNotes, CancellationToken ct) =>
        Back(await orders.UpdateStatusAsync(id, status, fulfilmentStatus, internalNotes, ct),
            nameof(Detail), new { id });

    [HttpPost("{id}/lab")]
    public async Task<IActionResult> UpdateLab(string id, string orderItemId, string labStatus,
        string? labRef, CancellationToken ct) =>
        Back(await orders.UpdateLabStatusAsync(orderItemId, labStatus, labRef, ct),
            nameof(Detail), new { id });

    [HttpPost("{id}/payment")]
    public async Task<IActionResult> MarkPaid(string id, string? reference, CancellationToken ct) =>
        Back(await orders.RecordManualPaymentAsync(id, reference, ct), nameof(Detail), new { id });

    [HttpPost("{id}/refund")]
    public async Task<IActionResult> Refund(string id, string paymentId, decimal? amount,
        CancellationToken ct) =>
        Back(await orders.RefundAsync(paymentId, amount, ct), nameof(Detail), new { id });

    [HttpPost("{id}/ship")]
    public async Task<IActionResult> Ship(string id, string carrier, string? trackingNumber,
        string? trackingUrl, string? rateRef, CancellationToken ct) =>
        Back(await orders.CreateShipmentAsync(id, carrier, trackingNumber, trackingUrl, rateRef, ct),
            nameof(Detail), new { id });
}

[Route("admin/patients")]
public class PatientsController(
    IPatientAdminService patients,
    UserManager<ApplicationUser> users) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? q, [FromQuery] bool pending,
        [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var filters = new PatientFilters { Q = q, PendingRxOnly = pending, Page = page };
        return View(new PatientListViewModel
        {
            Results = await patients.ListAsync(filters, ct),
            Filters = filters,
        });
    }

    [HttpGet("new")]
    public IActionResult New() => View("Edit", new PatientEditViewModel { Details = new PatientDetails() });

    [HttpPost("new")]
    public async Task<IActionResult> Create([FromForm] PatientDetails details, CancellationToken ct)
    {
        var result = await patients.CreateAsync(details, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View("Edit", new PatientEditViewModel { Details = details });
        }
        return RedirectToAction(nameof(Detail), new { id = result.Value });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Detail(string id, CancellationToken ct)
    {
        var patient = await patients.GetAsync(id, ct);
        return patient is null ? NotFound() : View(patient);
    }

    [HttpPost("{id}")]
    public async Task<IActionResult> Update(string id, [FromForm] PatientDetails details,
        CancellationToken ct) =>
        Back(await patients.UpdateAsync(id, details, ct), nameof(Detail), new { id });

    [HttpPost("{id}/prescriptions")]
    public async Task<IActionResult> AddPrescription(string id, [FromForm] PrescriptionForm form,
        CancellationToken ct)
    {
        var result = await patients.AddPrescriptionAsync(id, form.ToInput(), form.Source, ct);
        if (result.Ok) TempData["AdminOk"] = "Prescription added.";
        else TempData["AdminError"] = result.Error;
        return RedirectToAction(nameof(Detail), new { id });
    }

    /// <summary>
    /// Verification is the clinical gate: nothing reaches the lab without it.
    /// Restricted to opticians and administrators — general staff may see the
    /// queue but may not sign a prescription off.
    /// </summary>
    [HttpPost("{id}/prescriptions/{prescriptionId}/verify")]
    [Authorize(Policy = AuthorizationPolicies.OpticianOnly)]
    public async Task<IActionResult> Verify(string id, string prescriptionId, CancellationToken ct) =>
        Back(await patients.VerifyAsync(prescriptionId, users.GetUserId(User)!, ct),
            nameof(Detail), new { id });

    [HttpPost("{id}/prescriptions/{prescriptionId}/reject")]
    [Authorize(Policy = AuthorizationPolicies.OpticianOnly)]
    public async Task<IActionResult> Reject(string id, string prescriptionId, string? reason,
        CancellationToken ct) =>
        Back(await patients.RejectAsync(prescriptionId, reason, ct), nameof(Detail), new { id });
}

[Route("admin/frames")]
public class FramesController(
    ICatalogueAdminService catalogue,
    IApplicationDbContext db) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? q, [FromQuery] string? status,
        [FromQuery] int page = 1, CancellationToken ct = default)
    {
        return View(new FrameListViewModel
        {
            Results = await catalogue.ListFramesAsync(q, status, page, ct),
            Q = q,
            Status = status,
        });
    }

    [HttpGet("new")]
    public async Task<IActionResult> New(CancellationToken ct) =>
        View("Edit", await BuildEditModelAsync(null, ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var model = await BuildEditModelAsync(id, ct);
        return model.Frame is null && id is not null ? NotFound() : View(model);
    }

    [HttpPost("save")]
    public async Task<IActionResult> Save(string? id, [FromForm] FrameDetails details,
        [FromForm] List<string>? categoryIds, CancellationToken ct)
    {
        details.CategoryIds = categoryIds ?? [];
        var result = await catalogue.SaveFrameAsync(id, details, ct);

        if (!result.Ok)
        {
            TempData["AdminError"] = result.Error;
            var model = await BuildEditModelAsync(id, ct);
            model.Details = details;
            return View("Edit", model);
        }

        TempData["AdminOk"] = "Frame saved.";
        return RedirectToAction(nameof(Edit), new { id = result.Value });
    }

    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id, CancellationToken ct) =>
        Back(await catalogue.ArchiveFrameAsync(id, ct), nameof(Index));

    [HttpPost("{id}/variants")]
    public async Task<IActionResult> SaveVariant(string id, string? variantId,
        [FromForm] VariantDetails details, CancellationToken ct)
    {
        var result = await catalogue.SaveVariantAsync(id, variantId, details, ct);
        if (result.Ok) TempData["AdminOk"] = "Colourway saved.";
        else TempData["AdminError"] = result.Error;
        return RedirectToAction(nameof(Edit), new { id });
    }

    /// <summary>
    /// Sets where the wearer's pupils must land inside this colourway's artwork.
    /// Without this a newly photographed frame can never appear in the mirror —
    /// the legacy application had this screen and the migration would be
    /// incomplete without it.
    /// </summary>
    [HttpPost("{id}/variants/{variantId}/calibrate")]
    public async Task<IActionResult> Calibrate(string id, string variantId,
        double anchorLeftX, double anchorLeftY, double anchorRightX, double anchorRightY,
        double tryOnScaleAdj, double tryOnOpacity, string? tryOnImageUrl, CancellationToken ct)
    {
        var result = await catalogue.SaveTryOnCalibrationAsync(
            variantId, anchorLeftX, anchorLeftY, anchorRightX, anchorRightY,
            tryOnScaleAdj, tryOnOpacity, tryOnImageUrl, ct);

        if (result.Ok) TempData["AdminOk"] = "Try-on calibration saved.";
        else TempData["AdminError"] = result.Error;
        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task<FrameEditViewModel> BuildEditModelAsync(string? id, CancellationToken ct)
    {
        var frame = id is null ? null : await catalogue.GetFrameAsync(id, ct);

        return new FrameEditViewModel
        {
            Frame = frame,
            Brands = await db.Brands.AsNoTracking().OrderBy(b => b.Name).ToListAsync(ct),
            Categories = await db.Categories.AsNoTracking().OrderBy(c => c.Position).ToListAsync(ct),
            Details = frame is null ? new FrameDetails() : FrameEditViewModel.From(frame),
            SelectedCategoryIds = frame?.Categories.Select(c => c.CategoryId).ToList() ?? [],
        };
    }
}

[Route("admin/lenses")]
public class LensesController(ICatalogueAdminService catalogue) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await catalogue.ListLensOptionsAsync(ct));

    [HttpPost("save")]
    public async Task<IActionResult> Save(string? id, [FromForm] LensOptionDetails details,
        CancellationToken ct) =>
        Back(await catalogue.SaveLensOptionAsync(id, details, ct), nameof(Index));

    [HttpPost("{id}/retire")]
    public async Task<IActionResult> Retire(string id, CancellationToken ct) =>
        Back(await catalogue.RetireLensOptionAsync(id, ct), nameof(Index));
}

[Route("admin/promotions")]
public class PromotionsController(IPlatformAdminService platform) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await platform.ListPromotionsAsync(ct));

    [HttpGet("new")]
    public IActionResult New() => View("Edit", new PromotionEditViewModel());

    [HttpGet("{id}")]
    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var promotion = await platform.GetPromotionAsync(id, ct);
        if (promotion is null) return NotFound();
        return View(new PromotionEditViewModel
        {
            Id = promotion.Id,
            Details = PromotionEditViewModel.From(promotion),
            UsageCount = promotion.UsageCount,
        });
    }

    [HttpPost("save")]
    public async Task<IActionResult> Save(string? id, [FromForm] PromotionDetails details,
        CancellationToken ct)
    {
        var result = await platform.SavePromotionAsync(id, details, ct);
        if (!result.Ok)
        {
            TempData["AdminError"] = result.Error;
            return View("Edit", new PromotionEditViewModel { Id = id, Details = details });
        }
        TempData["AdminOk"] = "Deal saved.";
        return RedirectToAction(nameof(Edit), new { id = result.Value });
    }

    [HttpPost("{id}/toggle")]
    public async Task<IActionResult> Toggle(string id, CancellationToken ct) =>
        Back(await platform.TogglePromotionAsync(id, ct), nameof(Index));
}

/// <summary>
/// Delivery rates. New in the migration — the legacy application read this table
/// but had no screen for it, so changing a delivery price meant editing the
/// database by hand.
/// </summary>
[Route("admin/shipping")]
public class ShippingRatesController(IPlatformAdminService platform) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await platform.ListShippingRatesAsync(ct));

    [HttpPost("save")]
    public async Task<IActionResult> Save(string? id, [FromForm] ShippingRateDetails details,
        CancellationToken ct) =>
        Back(await platform.SaveShippingRateAsync(id, details, ct), nameof(Index));

    [HttpPost("{id}/toggle")]
    public async Task<IActionResult> Toggle(string id, CancellationToken ct) =>
        Back(await platform.ToggleShippingRateAsync(id, ct), nameof(Index));
}

/// <summary>
/// The audit viewer. New in the migration — the trail was written in 26 places
/// and read by nothing, so "who changed this price?" needed a database session.
/// Administrators only: it spans every staff member's activity.
/// </summary>
[Route("admin/audit")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class AuditController(IPlatformAdminService platform) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? q, [FromQuery] string? action,
        [FromQuery] string? entity, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var filters = new AuditFilters
        {
            Q = q, Action = action, Entity = entity, From = from, To = to, Page = page,
        };

        return View(new AuditViewModel
        {
            Results = await platform.ListAuditAsync(filters, ct),
            Actions = await platform.AuditActionNamesAsync(ct),
            Entities = await platform.AuditEntityNamesAsync(ct),
            Filters = filters,
        });
    }
}

[Route("admin/settings")]
public class SettingsController(
    IPlatformAdminService platform,
    ISettingsService settings,
    Microsoft.Extensions.Options.IOptions<Application.Payments.PaymentOptions> payments,
    Microsoft.Extensions.Options.IOptions<Application.Shipping.ShippingOptions> shipping,
    Microsoft.Extensions.Options.IOptions<Application.Email.EmailOptions> email,
    Microsoft.Extensions.Options.IOptions<Application.Pricing.StoreOptions> store,
    Microsoft.Extensions.Options.IOptions<Application.Pricing.TaxOptions> tax) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(new SettingsViewModel
    {
        Values = await settings.GetAllAsync(ct),
        PaymentProviders = payments.Value.Providers,
        StripeConfigured = !string.IsNullOrWhiteSpace(payments.Value.StripeSecretKey),
        ShippingProvider = shipping.Value.Provider,
        EmailDriver = email.Value.Driver,
        Currency = store.Value.Currency,
        TaxRateBps = tax.Value.RateBps,
        TaxInclusive = tax.Value.Inclusive,
    });

    [HttpPost("")]
    public async Task<IActionResult> Save(CancellationToken ct)
    {
        var values = Request.Form
            .Where(f => f.Key.StartsWith("setting.", StringComparison.Ordinal))
            .ToDictionary(f => f.Key["setting.".Length..], f => f.Value.ToString());

        var booleanKeys = Request.Form["__booleans"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return Back(await platform.SaveSettingsAsync(values, booleanKeys, ct), nameof(Index));
    }
}
