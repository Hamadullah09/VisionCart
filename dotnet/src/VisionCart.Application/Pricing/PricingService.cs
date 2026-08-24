using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VisionCart.Application.Common;
using VisionCart.Application.Prescriptions;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;
using VisionCart.Domain.ValueObjects;

namespace VisionCart.Application.Pricing;

public sealed class LineInput
{
    public string VariantId { get; init; } = string.Empty;
    public int Qty { get; init; } = 1;
    public IReadOnlyList<string> LensOptionCodes { get; init; } = [];
    public FlatRx? Rx { get; init; }
    /// <summary>Cart line id, carried through so the view can address the row.</summary>
    public string? ItemId { get; init; }
}

public sealed class PricedLine
{
    public string? ItemId { get; init; }
    public string VariantId { get; init; } = string.Empty;
    public int Qty { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }

    /// <summary>Kept on the line so promotion targeting never needs a second query.</summary>
    public string FrameId { get; init; } = string.Empty;
    public string? BrandId { get; init; }
    public IReadOnlyList<string> CategoryIds { get; init; } = [];

    public int FramePriceMinor { get; init; }
    public int LensPriceMinor { get; init; }
    /// <summary>(frame + lens) x qty</summary>
    public int TotalMinor { get; init; }

    public IReadOnlyList<LensOption> LensOptions { get; init; } = [];
    public string LensSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Per-unit price, used by the buy-one-get-one calculation.</summary>
    public int UnitGoodsMinor => FramePriceMinor + LensPriceMinor;
    public int LineGoodsMinor => UnitGoodsMinor * Qty;
}

public sealed class Totals
{
    public string Currency { get; init; } = "PKR";
    public int SubtotalMinor { get; init; }
    public int LensTotalMinor { get; init; }
    public int DiscountMinor { get; init; }
    public int ShippingMinor { get; init; }
    public int TaxMinor { get; init; }
    public int TotalMinor { get; init; }
}

/// <summary>Tax configuration, read from application settings rather than env vars.</summary>
public sealed class TaxOptions
{
    public const string SectionName = "Tax";
    /// <summary>Percent as basis points: 1700 = 17%. Zero disables tax lines.</summary>
    public int RateBps { get; set; }
    /// <summary>True when displayed prices already contain tax.</summary>
    public bool Inclusive { get; set; }
}

public interface IPricingService
{
    Task<IReadOnlyList<PricedLine>> PriceLinesAsync(IReadOnlyList<LineInput> inputs, CancellationToken ct = default);
    Task<IReadOnlyList<LensOption>> LoadLensOptionsAsync(IReadOnlyList<string> codes, CancellationToken ct = default);
    IReadOnlyList<string> ValidateLensSelection(IReadOnlyList<LensOption> options, FlatRx? rx);
    Totals ComputeTotals(IReadOnlyList<PricedLine> lines, int discountMinor, int shippingMinor, string? currency = null);
}

/// <summary>
/// Port of <c>src/lib/pricing.ts</c>.
///
/// The single place where a price is decided. The storefront, the cart and the
/// order writer all call this — a line total is never computed in a view, so a
/// tampered client payload cannot change what is charged.
/// </summary>
public sealed class PricingService(IApplicationDbContext db, IOptions<TaxOptions> tax, IOptions<StoreOptions> store)
    : IPricingService
{
    private readonly TaxOptions _tax = tax.Value;
    private readonly StoreOptions _store = store.Value;

    public async Task<IReadOnlyList<LensOption>> LoadLensOptionsAsync(
        IReadOnlyList<string> codes, CancellationToken ct = default)
    {
        if (codes.Count == 0) return [];
        return await db.LensOptions
            .Where(o => codes.Contains(o.Code) && o.IsActive)
            .OrderBy(o => o.Group).ThenBy(o => o.Position)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Reject option combinations the lab cannot make. Returns human-readable
    /// problems rather than throwing, so the UI can show them next to the choice.
    /// </summary>
    public IReadOnlyList<string> ValidateLensSelection(IReadOnlyList<LensOption> options, FlatRx? rx)
    {
        var problems = new List<string>();
        var codes = options.Select(o => o.Code).ToHashSet(StringComparer.Ordinal);

        foreach (var opt in options)
        {
            foreach (var required in SplitCodes(opt.Requires))
                if (!codes.Contains(required))
                    problems.Add($"{opt.Name} also requires \"{required}\".");

            foreach (var excluded in SplitCodes(opt.Excludes))
                if (codes.Contains(excluded))
                    problems.Add($"{opt.Name} cannot be combined with \"{excluded}\".");

            if (rx is not null)
            {
                var sph = Rx.StrongestSphere(rx);
                var cyl = Rx.StrongestCylinder(rx);

                if (opt.MinSphere is { } min && sph < Math.Abs(min))
                    problems.Add($"{opt.Name} is only available from {min:F2} D.");

                if (opt.MaxSphere is { } max && sph > Math.Abs(max))
                    problems.Add(
                        $"{opt.Name} tops out at {Math.Abs(max):F2} D — your prescription is {sph:F2} D. " +
                        "Pick a thinner index.");

                if (opt.MaxCylinder is { } maxCyl && cyl > Math.Abs(maxCyl))
                    problems.Add($"{opt.Name} supports a cylinder up to {Math.Abs(maxCyl):F2} D.");
            }
            else if (LensGroups.RxOnly.Contains(opt.Group))
            {
                problems.Add($"{opt.Name} needs a prescription on the order.");
            }
        }

        return problems;
    }

    public async Task<IReadOnlyList<PricedLine>> PriceLinesAsync(
        IReadOnlyList<LineInput> inputs, CancellationToken ct = default)
    {
        if (inputs.Count == 0) return [];

        var variantIds = inputs.Select(i => i.VariantId).Distinct().ToList();

        // One query for every variant, with its frame, categories and lead image.
        // The legacy implementation did the same; loading per line would be N+1.
        var variants = await db.FrameVariants
            .AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Include(v => v.Frame).ThenInclude(f => f.Categories)
            .Include(v => v.Images.OrderBy(i => i.Position).Take(1))
            .ToListAsync(ct);

        var byId = variants.ToDictionary(v => v.Id, StringComparer.Ordinal);

        var allCodes = inputs.SelectMany(i => i.LensOptionCodes).Distinct().ToList();
        var options = await LoadLensOptionsAsync(allCodes, ct);
        var optionByCode = options.ToDictionary(o => o.Code, StringComparer.Ordinal);

        var lines = new List<PricedLine>(inputs.Count);

        foreach (var input in inputs)
        {
            // Silently drop lines whose product was deleted, matching the legacy
            // behaviour — the customer sees the line vanish rather than an error.
            if (!byId.TryGetValue(input.VariantId, out var variant)) continue;

            var qty = Math.Clamp(input.Qty, 1, 20);
            var framePriceMinor = variant.PriceMinor ?? variant.Frame.BasePriceMinor;

            var chosen = input.LensOptionCodes
                .Select(c => optionByCode.GetValueOrDefault(c))
                .Where(o => o is not null)
                .Select(o => o!)
                .ToList();

            var lensPriceMinor = chosen.Sum(o => o.PriceMinor);

            var warnings = new List<string>(ValidateLensSelection(chosen, input.Rx));

            if (variant.StockQty <= 0)
                warnings.Add($"{variant.Frame.Name} ({variant.ColorName}) is out of stock.");
            else if (variant.StockQty < qty)
                warnings.Add($"Only {variant.StockQty} left of {variant.Frame.Name} ({variant.ColorName}).");

            if (variant.Frame.RequiresPrescription && input.Rx is null && chosen.Count == 0)
                warnings.Add($"{variant.Frame.Name} is sold with prescription lenses only.");

            lines.Add(new PricedLine
            {
                ItemId = input.ItemId,
                VariantId = variant.Id,
                Qty = qty,
                Title = $"{variant.Frame.Name} — {variant.ColorName}",
                Sku = variant.Sku,
                ImageUrl = variant.Images.FirstOrDefault()?.ThumbUrl
                           ?? variant.Images.FirstOrDefault()?.Url,
                FrameId = variant.FrameId,
                BrandId = variant.Frame.BrandId,
                CategoryIds = variant.Frame.Categories.Select(c => c.CategoryId).ToList(),
                FramePriceMinor = framePriceMinor,
                LensPriceMinor = lensPriceMinor,
                TotalMinor = (framePriceMinor + lensPriceMinor) * qty,
                LensOptions = chosen,
                LensSummary = chosen.Count > 0
                    ? string.Join(" · ", chosen.Select(o => o.Name))
                    : "Frame only",
                Warnings = warnings,
            });
        }

        return lines;
    }

    /// <summary>
    /// Tax is charged on goods after discount but before shipping, which is the
    /// common arrangement. Inclusive mode instead treats displayed prices as
    /// already containing tax and just reports the embedded portion.
    /// </summary>
    public Totals ComputeTotals(
        IReadOnlyList<PricedLine> lines, int discountMinor, int shippingMinor, string? currency = null)
    {
        var subtotalMinor = lines.Sum(l => l.FramePriceMinor * l.Qty);
        var lensTotalMinor = lines.Sum(l => l.LensPriceMinor * l.Qty);
        var goods = subtotalMinor + lensTotalMinor;

        var appliedDiscount = Math.Min(discountMinor, goods);
        var taxable = Money.ClampNonNegative(goods - appliedDiscount);

        var bps = _tax.RateBps;
        var taxMinor = bps > 0
            ? _tax.Inclusive
                ? taxable - (int)Math.Round((decimal)taxable * 10000 / (10000 + bps), MidpointRounding.AwayFromZero)
                : Money.ApplyBps(taxable, bps)
            : 0;

        var totalMinor = _tax.Inclusive
            ? taxable + shippingMinor
            : taxable + shippingMinor + taxMinor;

        return new Totals
        {
            Currency = currency ?? _store.Currency,
            SubtotalMinor = subtotalMinor,
            LensTotalMinor = lensTotalMinor,
            DiscountMinor = appliedDiscount,
            ShippingMinor = shippingMinor,
            TaxMinor = taxMinor,
            TotalMinor = Money.ClampNonNegative(totalMinor),
        };
    }

    public static IReadOnlyList<string> SplitCodes(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public static string JoinCodes(IEnumerable<string> codes) =>
        string.Join(",", codes.Where(c => !string.IsNullOrWhiteSpace(c)));
}

/// <summary>Storefront identity and currency, previously NEXT_PUBLIC_* env vars.</summary>
public sealed class StoreOptions
{
    public const string SectionName = "Store";
    public string Name { get; set; } = "VisionCart Optical";
    public string Currency { get; set; } = "PKR";
    public string CurrencySymbol { get; set; } = "Rs.";
    public string AppUrl { get; set; } = "https://localhost:5001";
}
