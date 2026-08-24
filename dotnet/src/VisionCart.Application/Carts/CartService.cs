using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VisionCart.Application.Common;
using VisionCart.Application.Platform;
using VisionCart.Application.Prescriptions;
using VisionCart.Application.Pricing;
using VisionCart.Application.Promotions;
using VisionCart.Application.Shipping;
using VisionCart.Domain.Entities;

namespace VisionCart.Application.Carts;

public sealed class CartView
{
    public string CartId { get; init; } = string.Empty;
    public IReadOnlyList<PricedLine> Lines { get; init; } = [];
    public Totals Totals { get; init; } = new();
    public IReadOnlyList<AppliedPromotion> Promotions { get; init; } = [];
    public string? PromoCode { get; init; }
    public string? PromoError { get; init; }
    public int ItemCount { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool IsEmpty => Lines.Count == 0;
}

public sealed class AddToCartRequest
{
    public string VariantId { get; init; } = string.Empty;
    public int Qty { get; init; } = 1;
    public IReadOnlyList<string> LensOptionCodes { get; init; } = [];
    public PrescriptionInput? PrescriptionDraft { get; init; }
    public string? PrescriptionId { get; init; }
    public string? TryOnSnapshotId { get; init; }
}

/// <summary>
/// Reads and writes the cart cookie. Implemented in the web layer; the cart
/// service itself must not know about HTTP.
/// </summary>
public interface ICartTokenAccessor
{
    string? Read();
    void Write(string token);
    void Clear();
}

public interface ICartService
{
    Task<Cart> GetOrCreateAsync(CancellationToken ct = default);
    Task<Cart?> PeekAsync(CancellationToken ct = default);
    Task<CartView> BuildViewAsync(string cartId, string? country = null, CancellationToken ct = default);
    Task<ActionResult> AddAsync(AddToCartRequest request, CancellationToken ct = default);
    Task<ActionResult> UpdateQuantityAsync(string itemId, int qty, CancellationToken ct = default);
    Task<ActionResult> RemoveAsync(string itemId, CancellationToken ct = default);
    Task<ActionResult> ApplyPromoAsync(string? code, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    void ClearCookie();
    static FlatRx? ParseRxDraft(string? raw) => ParseDraft(raw);

    private static FlatRx? ParseDraft(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return JsonSerializer.Deserialize<FlatRx>(raw); }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// Port of <c>src/lib/cart.ts</c> and <c>src/app/actions/cart.ts</c>.
///
/// The cart is priced from scratch on every read. Slower than trusting the
/// stored numbers, but it means a price change, a stock change or a promotion
/// ending is reflected the moment the customer looks at their bag — and a
/// tampered payload cannot alter what is charged.
/// </summary>
public sealed class CartService(
    IApplicationDbContext db,
    ICartTokenAccessor tokens,
    ICurrentUser currentUser,
    IPricingService pricing,
    IPromotionService promotions,
    IShippingService shipping,
    IOptions<StoreOptions> store) : ICartService
{
    private const int MaxQtyPerLine = 20;
    private readonly StoreOptions _store = store.Value;

    public async Task<Cart?> PeekAsync(CancellationToken ct = default)
    {
        var token = tokens.Read();
        if (string.IsNullOrEmpty(token)) return null;
        return await db.Carts.FirstOrDefaultAsync(c => c.Token == token, ct);
    }

    public async Task<Cart> GetOrCreateAsync(CancellationToken ct = default)
    {
        var token = tokens.Read();

        if (!string.IsNullOrEmpty(token))
        {
            var existing = await db.Carts.FirstOrDefaultAsync(c => c.Token == token, ct);
            if (existing is not null)
            {
                // Someone who shopped as a guest and then signed in keeps their bag.
                if (currentUser.IsAuthenticated && existing.UserId is null)
                {
                    existing.UserId = currentUser.UserId;
                    await db.SaveChangesAsync(ct);
                }
                return existing;
            }
        }

        var newToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var cart = new Cart
        {
            Token = newToken,
            UserId = currentUser.UserId,
            Currency = _store.Currency,
        };

        db.Carts.Add(cart);
        await db.SaveChangesAsync(ct);
        tokens.Write(newToken);
        return cart;
    }

    public async Task<CartView> BuildViewAsync(
        string cartId, string? country = null, CancellationToken ct = default)
    {
        var cart = await db.Carts
            .AsNoTracking()
            .Include(c => c.Items.OrderBy(i => i.CreatedAt))
            .FirstOrDefaultAsync(c => c.Id == cartId, ct);

        if (cart is null)
        {
            return new CartView
            {
                CartId = cartId,
                Totals = pricing.ComputeTotals([], 0, 0, _store.Currency),
            };
        }

        var inputs = cart.Items.Select(i => new LineInput
        {
            ItemId = i.Id,
            VariantId = i.VariantId,
            Qty = i.Qty,
            LensOptionCodes = PricingService.SplitCodes(i.LensOptionCodes),
            Rx = ICartService.ParseRxDraft(i.PrescriptionDraft),
        }).ToList();

        var lines = await pricing.PriceLinesAsync(inputs, ct);

        var promo = await promotions.EvaluateAsync(
            lines, cart.PromoCode, currentUser.UserId, currentUser.Email, ct);

        var goods = lines.Sum(l => l.TotalMinor);
        var quotes = await shipping.QuoteAsync(new ShippingQuoteRequest
        {
            SubtotalMinor = goods - promo.DiscountMinor,
            Country = country ?? "PK",
            ItemCount = lines.Sum(l => l.Qty),
        }, ct);

        var shippingMinor = promo.FreeShipping ? 0 : quotes.FirstOrDefault()?.PriceMinor ?? 0;

        return new CartView
        {
            CartId = cart.Id,
            Lines = lines,
            Totals = pricing.ComputeTotals(lines, promo.DiscountMinor, shippingMinor, cart.Currency),
            Promotions = promo.Applied,
            PromoCode = cart.PromoCode,
            PromoError = promo.CodeError,
            ItemCount = lines.Sum(l => l.Qty),
            Warnings = [.. lines.SelectMany(l => l.Warnings)],
        };
    }

    public async Task<ActionResult> AddAsync(AddToCartRequest request, CancellationToken ct = default)
    {
        var variant = await db.FrameVariants
            .AsNoTracking()
            .Include(v => v.Frame)
            .FirstOrDefaultAsync(v => v.Id == request.VariantId && v.IsActive, ct);

        if (variant is null) return ActionResult.Fail("That frame is no longer available.");
        if (variant.StockQty <= 0) return ActionResult.Fail($"{variant.Frame.Name} ({variant.ColorName}) is out of stock.");

        // Validate the draft prescription before it can reach a lab ticket.
        if (request.PrescriptionDraft is not null)
        {
            var validation = Rx.Validate(request.PrescriptionDraft);
            if (!validation.IsValid)
                return ActionResult.Fail(validation.Errors[0].Message);
        }

        // Sorted so the same lens build always produces the same key, whatever
        // order the customer ticked the boxes in.
        var codes = PricingService.JoinCodes(request.LensOptionCodes.Order(StringComparer.Ordinal));
        var normalisedCodes = string.IsNullOrEmpty(codes) ? null : codes;

        var cart = await GetOrCreateAsync(ct);

        // Same frame + same lens build = bump the quantity instead of stacking rows.
        var twin = await db.CartItems.FirstOrDefaultAsync(i =>
            i.CartId == cart.Id
            && i.VariantId == request.VariantId
            && i.LensOptionCodes == normalisedCodes
            && i.PrescriptionId == request.PrescriptionId, ct);

        if (twin is not null && request.PrescriptionDraft is null)
        {
            twin.Qty = Math.Min(MaxQtyPerLine, twin.Qty + Math.Max(1, request.Qty));
        }
        else
        {
            db.CartItems.Add(new CartItem
            {
                CartId = cart.Id,
                VariantId = request.VariantId,
                Qty = Math.Clamp(request.Qty, 1, MaxQtyPerLine),
                LensOptionCodes = normalisedCodes,
                PrescriptionDraft = request.PrescriptionDraft is null
                    ? null
                    : JsonSerializer.Serialize(Rx.ToFlat(request.PrescriptionDraft)),
                PrescriptionId = request.PrescriptionId,
                TryOnSnapshotId = request.TryOnSnapshotId,
            });
        }

        cart.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ActionResult.Success();
    }

    public async Task<ActionResult> UpdateQuantityAsync(string itemId, int qty, CancellationToken ct = default)
    {
        var item = await OwnedItemAsync(itemId, ct);
        if (item is null) return ActionResult.Fail("That item is no longer in your bag.");

        if (qty <= 0) db.CartItems.Remove(item);
        else item.Qty = Math.Min(MaxQtyPerLine, qty);

        await db.SaveChangesAsync(ct);
        return ActionResult.Success();
    }

    public async Task<ActionResult> RemoveAsync(string itemId, CancellationToken ct = default)
    {
        var item = await OwnedItemAsync(itemId, ct);
        if (item is null) return ActionResult.Fail("That item is no longer in your bag.");

        db.CartItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return ActionResult.Success();
    }

    public async Task<ActionResult> ApplyPromoAsync(string? code, CancellationToken ct = default)
    {
        var cart = await GetOrCreateAsync(ct);
        cart.PromoCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
        await db.SaveChangesAsync(ct);

        if (cart.PromoCode is null) return ActionResult.Success();

        // Report immediately whether the code actually did anything, rather than
        // storing it silently and leaving the customer to spot the missing line.
        var view = await BuildViewAsync(cart.Id, ct: ct);
        return view.PromoError is null
            ? ActionResult.Success()
            : ActionResult.Fail(view.PromoError);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        var cart = await PeekAsync(ct);
        if (cart is null) return 0;
        return await db.CartItems.Where(i => i.CartId == cart.Id).SumAsync(i => i.Qty, ct);
    }

    public void ClearCookie() => tokens.Clear();

    /// <summary>
    /// Resolves a cart line, but only within the caller's own cart — without it
    /// an item id from another customer's bag would be editable. The legacy
    /// application made the same check, but in its action layer rather than its
    /// cart library, so every new caller had to remember to repeat it. Putting it
    /// on the lookup makes it structural.
    /// </summary>
    private async Task<CartItem?> OwnedItemAsync(string itemId, CancellationToken ct)
    {
        var cart = await PeekAsync(ct);
        if (cart is null) return null;
        return await db.CartItems.FirstOrDefaultAsync(i => i.Id == itemId && i.CartId == cart.Id, ct);
    }
}
