using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Carts;
using VisionCart.Application.Catalogue;
using VisionCart.Application.Checkout;
using VisionCart.Application.Common;
using VisionCart.Application.Payments;
using VisionCart.Application.Platform;
using VisionCart.Application.Prescriptions;
using VisionCart.Application.Promotions;
using VisionCart.Domain.Entities;
using VisionCart.Web.Models;

namespace VisionCart.Web.Controllers;

public class HomeController(
    ICatalogService catalog,
    IPromotionService promotions) : Controller
{
    [HttpGet("/")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = new HomeViewModel
        {
            Featured = await catalog.FeaturedAsync(12, ct),
            Banners = await promotions.ActiveBannersAsync(ct),
            Deals = await promotions.LiveDealsAsync(ct),
        };
        return View(model);
    }

    [HttpGet("/deals")]
    public async Task<IActionResult> Deals(CancellationToken ct) =>
        View(await promotions.LiveDealsAsync(ct));
}

[Route("frames")]
public class FramesController(ICatalogService catalog) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? q, [FromQuery] string? gender, [FromQuery] string? shape,
        [FromQuery] string? material, [FromQuery] string? rimType, [FromQuery] string? brand,
        [FromQuery] string? category, [FromQuery] string? sizeBand, [FromQuery] string? sort,
        [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var filters = new FrameFilters
        {
            Q = q, Gender = gender, Shape = shape, Material = material, RimType = rimType,
            Brand = brand, Category = category, SizeBand = sizeBand, Sort = sort, Page = page,
        };

        return View(new CatalogueViewModel
        {
            Results = await catalog.ListFramesAsync(filters, ct),
            Facets = await catalog.GetFacetsAsync(ct),
            Filters = filters,
        });
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Detail(string slug, CancellationToken ct)
    {
        var frame = await catalog.GetBySlugAsync(slug, ct);
        if (frame is null) return NotFound();

        return View(new ProductViewModel
        {
            Frame = frame,
            LensOptions = await catalog.LensBuilderOptionsAsync(ct),
        });
    }
}

[Route("cart")]
public class CartController(ICartService carts) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var cart = await carts.PeekAsync(ct);
        if (cart is null) return View(new CartView());

        return View(await carts.BuildViewAsync(cart.Id, ct: ct));
    }

    [HttpPost("add")]
    public async Task<IActionResult> Add([FromForm] AddToCartForm form, CancellationToken ct)
    {
        var request = new AddToCartRequest
        {
            VariantId = form.VariantId,
            Qty = form.Qty,
            LensOptionCodes = form.LensOptionCodes ?? [],
            PrescriptionDraft = form.ToPrescriptionInput(),
        };

        var result = await carts.AddAsync(request, ct);

        if (!result.Ok)
        {
            TempData["CartError"] = result.Error;
            return Redirect(form.ReturnUrl ?? "/frames");
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("update")]
    public async Task<IActionResult> Update(string itemId, int qty, CancellationToken ct)
    {
        var result = await carts.UpdateQuantityAsync(itemId, qty, ct);
        if (!result.Ok) TempData["CartError"] = result.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("remove")]
    public async Task<IActionResult> Remove(string itemId, CancellationToken ct)
    {
        var result = await carts.RemoveAsync(itemId, ct);
        if (!result.Ok) TempData["CartError"] = result.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("promo")]
    public async Task<IActionResult> Promo(string? code, CancellationToken ct)
    {
        var result = await carts.ApplyPromoAsync(code, ct);
        if (!result.Ok) TempData["CartError"] = result.Error;
        return RedirectToAction(nameof(Index));
    }
}

[Route("checkout")]
public class CheckoutController(
    ICartService carts,
    ICheckoutService checkout,
    IPaymentService payments,
    ISettingsService settings) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var cart = await carts.PeekAsync(ct);
        if (cart is null) return RedirectToAction("Index", "Cart");

        var view = await carts.BuildViewAsync(cart.Id, ct: ct);
        if (view.IsEmpty) return RedirectToAction("Index", "Cart");

        return View(await BuildModelAsync(view, new CheckoutInput(), ct));
    }

    [HttpPost("")]
    [EnableRateLimiting("checkout")]
    public async Task<IActionResult> Place([FromForm] CheckoutInput input, CancellationToken ct)
    {
        var cart = await carts.PeekAsync(ct);
        if (cart is null) return RedirectToAction("Index", "Cart");

        var view = await carts.BuildViewAsync(cart.Id, input.Country, ct);

        if (!ModelState.IsValid)
            return View(nameof(Index), await BuildModelAsync(view, input, ct));

        var outcome = await checkout.PlaceOrderAsync(input, ct);

        if (!outcome.Ok)
        {
            ModelState.AddModelError(string.Empty, outcome.Error ?? "We couldn't place that order.");
            return View(nameof(Index), await BuildModelAsync(view, input, ct));
        }

        return Redirect(outcome.RedirectUrl!);
    }

    private async Task<CheckoutViewModel> BuildModelAsync(
        CartView view, CheckoutInput input, CancellationToken ct) => new()
    {
        Cart = view,
        Input = input,
        PaymentMethods = payments.EnabledMethods(),
        ShippingQuotes = await checkout.ShippingQuotesAsync(
            string.IsNullOrWhiteSpace(input.Country) ? "PK" : input.Country, input.State, ct),
        GuestAllowed = await settings.GetBoolAsync(SettingKeys.CheckoutGuestAllowed, ct),
    };
}

[Route("order")]
public class OrderController(IApplicationDbContext db, ICurrentUser currentUser) : Controller
{
    [HttpGet("{orderNo}")]
    public async Task<IActionResult> Detail(string orderNo, CancellationToken ct)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.ShippingAddress)
            .Include(o => o.Payments)
            .Include(o => o.Shipments)
            .FirstOrDefaultAsync(o => o.OrderNo == orderNo, ct);

        if (order is null) return NotFound();

        // A signed-in customer may only see their own orders. A guest order is
        // reachable by its number alone, which is how the legacy system worked —
        // the number is long and unguessable, and a guest has no account to log
        // into. Staff access goes through the back office, not this route.
        if (currentUser.IsAuthenticated && order.UserId is not null && order.UserId != currentUser.UserId)
            return NotFound();

        return View(order);
    }
}

[Route("error")]
public class ErrorController : Controller
{
    /// <summary>
    /// Custom error pages, replacing the framework defaults the legacy
    /// application fell back to. Never renders exception detail.
    /// </summary>
    [HttpGet("{code:int}")]
    public IActionResult Status(int code) => View("Status", new ErrorViewModel
    {
        StatusCode = code,
        Title = code switch
        {
            403 => "You don't have access to that",
            404 => "We couldn't find that page",
            429 => "Too many attempts",
            500 => "Something went wrong at our end",
            _ => "Something went wrong",
        },
        Message = code switch
        {
            403 => "Your account doesn't have permission to view this page.",
            404 => "The page may have moved, or the link may be out of date.",
            429 => "Please wait a few minutes and try again.",
            500 => "We've logged the problem. Please try again in a moment.",
            _ => "Please try again, or get in touch if it keeps happening.",
        },
    });
}
