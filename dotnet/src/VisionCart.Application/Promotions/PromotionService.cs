using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Common;
using VisionCart.Application.Pricing;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;
using VisionCart.Domain.ValueObjects;

namespace VisionCart.Application.Promotions;

public sealed class AppliedPromotion
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Code { get; init; }
    public string Kind { get; init; } = string.Empty;
    public int DiscountMinor { get; init; }
    public bool FreeShipping { get; init; }
    public string Description { get; init; } = string.Empty;
}

public sealed class PromotionResult
{
    public IReadOnlyList<AppliedPromotion> Applied { get; init; } = [];
    public int DiscountMinor { get; init; }
    public bool FreeShipping { get; init; }
    /// <summary>Set when the customer typed a code that was rejected.</summary>
    public string? CodeError { get; init; }

    public static PromotionResult Empty => new();
}

public interface IPromotionService
{
    Task<PromotionResult> EvaluateAsync(
        IReadOnlyList<PricedLine> lines, string? code, string? userId, string? email,
        CancellationToken ct = default);

    Task<IReadOnlyList<Promotion>> ActiveBannersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Promotion>> LiveDealsAsync(CancellationToken ct = default);
    static string Describe(Promotion p) => DescribeInternal(p);

    private static string DescribeInternal(Promotion p) => p.Kind switch
    {
        PromotionKinds.PercentOff => $"{p.Value / 100.0:0.#}% off",
        PromotionKinds.AmountOff => "Money off your order",
        PromotionKinds.FreeShipping => "Free delivery",
        PromotionKinds.Bogo => "Buy one, get one free",
        PromotionKinds.FreeLensUpgrade => "Free lens upgrade",
        PromotionKinds.Bundle => "Bundle price",
        _ => p.Name,
    };
}

/// <summary>
/// Port of <c>src/lib/promotions.ts</c>.
///
/// Marketing configures rows in the Promotion table from the back office;
/// nothing here is hard-coded to a campaign, so a new deal is a form submission
/// rather than a deploy.
/// </summary>
public sealed class PromotionService(IApplicationDbContext db, TimeProvider clock) : IPromotionService
{
    public async Task<PromotionResult> EvaluateAsync(
        IReadOnlyList<PricedLine> lines, string? code, string? userId, string? email,
        CancellationToken ct = default)
    {
        if (lines.Count == 0) return PromotionResult.Empty;

        var now = clock.GetUtcNow().UtcDateTime;
        var normalisedCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

        // Automatic promotions, plus the one code the customer typed.
        var candidates = await db.Promotions
            .AsNoTracking()
            .Where(p => p.IsActive && (p.Code == null || (normalisedCode != null && p.Code == normalisedCode)))
            .OrderByDescending(p => p.Priority).ThenBy(p => p.CreatedAt)
            .ToListAsync(ct);

        var isFirstOrder = await IsFirstOrderAsync(userId, email, ct);

        string? codeError = null;
        var usable = new List<(Promotion Promo, int DiscountMinor, bool FreeShipping)>();

        foreach (var p in candidates)
        {
            var eligible = EligibleLines(p, lines);
            var eligibleSubtotal = eligible.Sum(l => l.LineGoodsMinor);
            var live = IsLive(p, now);
            var unmet = UnmetCondition(p, eligible, eligibleSubtotal, isFirstOrder);

            if (!live || unmet is not null)
            {
                // Only explain failures for a code the customer deliberately typed.
                if (p.Code is not null && p.Code == normalisedCode)
                {
                    codeError = !live
                        ? $"\"{p.Code}\" has expired or is no longer available."
                        : unmet;
                }
                continue;
            }

            if (p.UsageLimitPerUser is { } perUser && !string.IsNullOrEmpty(userId))
            {
                var used = await db.Orders.CountAsync(
                    o => o.PromotionId == p.Id && o.UserId == userId && o.Status != OrderStatuses.Cancelled, ct);

                if (used >= perUser)
                {
                    if (p.Code == normalisedCode) codeError = $"You have already used \"{p.Code}\".";
                    continue;
                }
            }

            var (minor, freeShipping) = DiscountFor(p, eligible);
            if (minor <= 0 && !freeShipping) continue;
            usable.Add((p, minor, freeShipping));
        }

        // Highest priority wins; lower-priority ones only ride along if stackable.
        var ordered = usable
            .OrderByDescending(u => u.Promo.Priority)
            .ThenByDescending(u => u.DiscountMinor)
            .ToList();

        var applied = new List<AppliedPromotion>();
        var appliedPromos = new List<Promotion>();

        for (var i = 0; i < ordered.Count; i++)
        {
            var u = ordered[i];

            // The best offer always lands. Anything after it needs every promotion
            // in play — itself included — to permit stacking.
            if (i > 0 && (!u.Promo.Stackable || !appliedPromos.All(p => p.Stackable))) continue;

            appliedPromos.Add(u.Promo);
            applied.Add(new AppliedPromotion
            {
                Id = u.Promo.Id,
                Name = u.Promo.Name,
                Code = u.Promo.Code,
                Kind = u.Promo.Kind,
                DiscountMinor = u.DiscountMinor,
                FreeShipping = u.FreeShipping,
                Description = string.IsNullOrWhiteSpace(u.Promo.Description)
                    ? IPromotionService.Describe(u.Promo)
                    : u.Promo.Description,
            });
        }

        return new PromotionResult
        {
            Applied = applied,
            DiscountMinor = applied.Sum(a => a.DiscountMinor),
            FreeShipping = applied.Any(a => a.FreeShipping),
            // A code that ended up applying is not an error.
            CodeError = applied.Any(a => a.Code == normalisedCode) ? null : codeError,
        };
    }

    private static bool IsLive(Promotion p, DateTime now)
    {
        if (!p.IsActive) return false;
        if (p.StartsAt is { } from && from > now) return false;
        if (p.EndsAt is { } to && to < now) return false;
        if (p.UsageLimit is { } limit && p.UsageCount >= limit) return false;
        return true;
    }

    /// <summary>Lines this promotion may discount. Empty targeting = everything.</summary>
    private static List<PricedLine> EligibleLines(Promotion p, IReadOnlyList<PricedLine> lines)
    {
        var brands = PricingService.SplitCodes(p.BrandIds);
        var categories = PricingService.SplitCodes(p.CategoryIds);
        var frames = PricingService.SplitCodes(p.FrameIds);

        if (brands.Count == 0 && categories.Count == 0 && frames.Count == 0)
            return [.. lines];

        return [.. lines.Where(l =>
            (l.BrandId is not null && brands.Contains(l.BrandId))
            || frames.Contains(l.FrameId)
            || l.CategoryIds.Any(categories.Contains))];
    }

    /// <summary>Why a promotion did not apply, in words a customer can act on.</summary>
    private static string? UnmetCondition(
        Promotion p, List<PricedLine> eligible, int eligibleSubtotal, bool isFirstOrder)
    {
        if (eligible.Count == 0) return "This code doesn't apply to anything in your bag.";
        if (eligibleSubtotal < p.MinSubtotalMinor) return $"Spend a little more to unlock \"{p.Name}\".";

        var qty = eligible.Sum(l => l.Qty);
        if (qty < p.MinQty)
            return $"\"{p.Name}\" needs at least {p.MinQty} item{(p.MinQty > 1 ? "s" : "")}.";

        if (p.FirstOrderOnly && !isFirstOrder) return $"\"{p.Name}\" is for first orders only.";
        return null;
    }

    private static (int Minor, bool FreeShipping) DiscountFor(Promotion p, List<PricedLine> eligible)
    {
        var eligibleSubtotal = eligible.Sum(l => l.LineGoodsMinor);
        var minor = 0;
        var freeShipping = false;

        switch (p.Kind)
        {
            case PromotionKinds.PercentOff:
                minor = Money.ApplyBps(eligibleSubtotal, p.Value);
                break;

            case PromotionKinds.AmountOff:
                minor = p.Value;
                break;

            case PromotionKinds.FreeShipping:
                freeShipping = true;
                break;

            case PromotionKinds.Bogo:
            {
                // Expand to a flat list of unit prices, sort ascending, and make
                // every second unit free — the customer always keeps the more
                // expensive one.
                var units = new List<int>();
                foreach (var line in eligible)
                    for (var i = 0; i < line.Qty; i++)
                        units.Add(line.UnitGoodsMinor);

                units.Sort();
                var freeCount = units.Count / 2;
                minor = units.Take(freeCount).Sum();
                break;
            }

            case PromotionKinds.FreeLensUpgrade:
                // Waives what the customer paid for lens choices, capped below.
                minor = eligible.Sum(l => l.LensPriceMinor * l.Qty);
                break;

            case PromotionKinds.Bundle:
                // `Value` is the bundle price the eligible items are re-priced to.
                minor = Math.Max(0, eligibleSubtotal - p.Value);
                break;
        }

        if (p.MaxDiscountMinor is { } cap) minor = Math.Min(minor, cap);
        return (Math.Max(0, Math.Min(minor, eligibleSubtotal)), freeShipping);
    }

    private async Task<bool> IsFirstOrderAsync(string? userId, string? email, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(email)) return true;

        var normalisedEmail = email?.ToLowerInvariant();

        var count = await db.Orders.CountAsync(o =>
            o.Status != OrderStatuses.Cancelled
            && ((userId != null && o.UserId == userId)
                || (normalisedEmail != null && o.Email == normalisedEmail)), ct);

        return count == 0;
    }

    /// <summary>Active, code-free promotions for the storefront banner strip.</summary>
    public async Task<IReadOnlyList<Promotion>> ActiveBannersAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.Promotions
            .AsNoTracking()
            .Where(p => p.IsActive
                        && p.BannerText != null
                        && (p.StartsAt == null || p.StartsAt <= now)
                        && (p.EndsAt == null || p.EndsAt >= now))
            .OrderByDescending(p => p.Priority)
            .Take(3)
            .ToListAsync(ct);
    }

    /// <summary>Everything currently on offer, for the deals page.</summary>
    public async Task<IReadOnlyList<Promotion>> LiveDealsAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.Promotions
            .AsNoTracking()
            .Where(p => p.IsActive
                        && (p.StartsAt == null || p.StartsAt <= now)
                        && (p.EndsAt == null || p.EndsAt >= now)
                        && (p.UsageLimit == null || p.UsageCount < p.UsageLimit))
            .OrderByDescending(p => p.Priority).ThenBy(p => p.CreatedAt)
            .ToListAsync(ct);
    }
}
