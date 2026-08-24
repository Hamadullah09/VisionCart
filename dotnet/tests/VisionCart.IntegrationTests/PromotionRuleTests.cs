using Microsoft.Extensions.DependencyInjection;
using VisionCart.Application.Common;
using VisionCart.Application.Pricing;
using VisionCart.Application.Promotions;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;
using VisionCart.Domain.ValueObjects;
using VisionCart.Infrastructure.Persistence;

namespace VisionCart.IntegrationTests;

/// <summary>
/// The promotion engine's rules, exercised against the real service.
///
/// The stacking rule in particular is the subtlest logic in the shop: the
/// highest-priority offer always lands, and anything after it joins only if
/// *every* promotion in play permits stacking. Getting it wrong either silently
/// doubles a headline discount or suppresses a delivery perk that should ride
/// along — neither would be obvious from looking at an order.
/// </summary>
[Collection("checkout")]
public class PromotionRuleTests(CheckoutFlowFixture fixture) : IDisposable
{
    private readonly List<string> _created = [];

    public void Dispose()
    {
        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Promotions.RemoveRange(db.Promotions.Where(p => _created.Contains(p.Id)));
        db.SaveChanges();
    }

    private async Task<Promotion> AddPromotionAsync(Promotion promotion)
    {
        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Promotions.Add(promotion);
        await db.SaveChangesAsync();
        _created.Add(promotion.Id);
        return promotion;
    }

    /// <summary>A two-unit basket at a known price, so the arithmetic is checkable by hand.</summary>
    private static List<PricedLine> Basket(int unitMinor = 650000, int qty = 2) =>
    [
        new()
        {
            VariantId = "v1", FrameId = "f1", Qty = qty,
            Title = "Test frame", Sku = "TEST",
            FramePriceMinor = unitMinor, LensPriceMinor = 0,
            TotalMinor = unitMinor * qty,
        },
    ];

    private async Task<PromotionResult> EvaluateAsync(
        IReadOnlyList<PricedLine> lines, string? code = null, string? email = null)
    {
        using var scope = fixture.NewScope();
        var promotions = scope.ServiceProvider.GetRequiredService<IPromotionService>();
        return await promotions.EvaluateAsync(lines, code, null, email ?? $"{Guid.NewGuid():N}@example.com");
    }

    private static Promotion NonStackable(string name, int priority, int percentBps) => new()
    {
        Name = name, Kind = PromotionKinds.PercentOff, Value = percentBps,
        Priority = priority, Stackable = false, IsActive = true, Code = null,
    };

    [Fact]
    public async Task The_highest_priority_offer_always_applies()
    {
        await AddPromotionAsync(NonStackable($"ZZ-low-{Guid.NewGuid():N}", 1, 500));
        var high = await AddPromotionAsync(NonStackable($"ZZ-high-{Guid.NewGuid():N}", 99, 2000));

        var result = await EvaluateAsync(Basket());

        Assert.Contains(result.Applied, a => a.Id == high.Id);
    }

    [Fact]
    public async Task A_non_stackable_headline_deal_never_silently_doubles_up()
    {
        // Two non-stackable offers both qualify. Only the better one may land.
        await AddPromotionAsync(NonStackable($"ZZ-a-{Guid.NewGuid():N}", 90, 2000));
        await AddPromotionAsync(NonStackable($"ZZ-b-{Guid.NewGuid():N}", 80, 1000));

        var result = await EvaluateAsync(Basket());

        var mine = result.Applied.Where(a => a.Name.StartsWith("ZZ-")).ToList();
        Assert.Single(mine);
    }

    [Fact]
    public async Task A_shipping_perk_rides_alongside_a_discount_when_both_permit_stacking()
    {
        var discount = await AddPromotionAsync(new Promotion
        {
            Name = $"ZZ-stackable-discount-{Guid.NewGuid():N}",
            Kind = PromotionKinds.PercentOff, Value = 1000,
            Priority = 50, Stackable = true, IsActive = true,
        });

        var freeDelivery = await AddPromotionAsync(new Promotion
        {
            Name = $"ZZ-stackable-shipping-{Guid.NewGuid():N}",
            Kind = PromotionKinds.FreeShipping,
            Priority = 10, Stackable = true, IsActive = true,
        });

        var result = await EvaluateAsync(Basket());

        Assert.Contains(result.Applied, a => a.Id == discount.Id);
        Assert.Contains(result.Applied, a => a.Id == freeDelivery.Id);
        Assert.True(result.FreeShipping);
    }

    [Fact]
    public async Task One_non_stackable_promotion_blocks_the_whole_stack()
    {
        // The rule is "every promotion in play must permit stacking", not "this
        // one does". A non-stackable winner suppresses the stackable runner-up.
        var headline = await AddPromotionAsync(NonStackable($"ZZ-headline-{Guid.NewGuid():N}", 95, 2500));

        var perk = await AddPromotionAsync(new Promotion
        {
            Name = $"ZZ-perk-{Guid.NewGuid():N}",
            Kind = PromotionKinds.FreeShipping,
            Priority = 5, Stackable = true, IsActive = true,
        });

        var result = await EvaluateAsync(Basket());

        Assert.Contains(result.Applied, a => a.Id == headline.Id);
        Assert.DoesNotContain(result.Applied, a => a.Id == perk.Id);
    }

    [Fact]
    public async Task Buy_one_get_one_makes_the_cheaper_unit_free()
    {
        await AddPromotionAsync(new Promotion
        {
            Name = $"ZZ-bogo-{Guid.NewGuid():N}",
            Kind = PromotionKinds.Bogo, Priority = 100, IsActive = true, MinQty = 2,
        });

        // Two units at Rs.6,500 → exactly one unit free.
        var result = await EvaluateAsync(Basket(unitMinor: 650000, qty: 2));

        var bogo = result.Applied.First(a => a.Kind == PromotionKinds.Bogo);
        Assert.Equal(650000, bogo.DiscountMinor);
    }

    [Fact]
    public async Task Buy_one_get_one_keeps_the_customer_the_more_expensive_pair()
    {
        await AddPromotionAsync(new Promotion
        {
            Name = $"ZZ-bogo-mixed-{Guid.NewGuid():N}",
            Kind = PromotionKinds.Bogo, Priority = 100, IsActive = true, MinQty = 2,
        });

        List<PricedLine> mixed =
        [
            new() { VariantId = "cheap", FrameId = "f1", Qty = 1, Title = "Cheap", Sku = "C",
                    FramePriceMinor = 400000, TotalMinor = 400000 },
            new() { VariantId = "dear", FrameId = "f2", Qty = 1, Title = "Dear", Sku = "D",
                    FramePriceMinor = 900000, TotalMinor = 900000 },
        ];

        var result = await EvaluateAsync(mixed);
        var bogo = result.Applied.First(a => a.Kind == PromotionKinds.Bogo);

        // The cheaper unit is the free one — never the expensive one.
        Assert.Equal(400000, bogo.DiscountMinor);
    }

    [Fact]
    public async Task A_discount_is_capped_when_a_maximum_is_configured()
    {
        await AddPromotionAsync(new Promotion
        {
            Name = $"ZZ-capped-{Guid.NewGuid():N}",
            Kind = PromotionKinds.PercentOff, Value = 5000, // 50%
            MaxDiscountMinor = 100000,                       // capped at Rs.1,000
            Priority = 100, IsActive = true,
        });

        var result = await EvaluateAsync(Basket(unitMinor: 650000, qty: 2));

        Assert.Equal(100000, result.DiscountMinor);
    }

    [Fact]
    public async Task A_discount_can_never_exceed_the_value_of_the_basket()
    {
        await AddPromotionAsync(new Promotion
        {
            Name = $"ZZ-huge-{Guid.NewGuid():N}",
            Kind = PromotionKinds.AmountOff, Value = 99_999_999,
            Priority = 100, IsActive = true,
        });

        var basket = Basket(unitMinor: 650000, qty: 1);
        var result = await EvaluateAsync(basket);

        Assert.Equal(650000, result.DiscountMinor);
    }

    [Fact]
    public async Task An_expired_code_is_rejected_with_a_reason_the_customer_can_act_on()
    {
        var code = $"ZZEXP{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        await AddPromotionAsync(new Promotion
        {
            Name = "ZZ-expired", Code = code,
            Kind = PromotionKinds.PercentOff, Value = 1000,
            EndsAt = DateTime.UtcNow.AddDays(-1), IsActive = true,
        });

        var result = await EvaluateAsync(Basket(), code);

        Assert.DoesNotContain(result.Applied, a => a.Code == code);
        Assert.NotNull(result.CodeError);
        Assert.Contains("expired", result.CodeError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_minimum_spend_that_is_not_met_explains_itself()
    {
        var code = $"ZZMIN{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        await AddPromotionAsync(new Promotion
        {
            Name = "ZZ-min-spend", Code = code,
            Kind = PromotionKinds.PercentOff, Value = 1000,
            MinSubtotalMinor = 10_000_000, IsActive = true,
        });

        var result = await EvaluateAsync(Basket(unitMinor: 100, qty: 1), code);

        Assert.NotNull(result.CodeError);
        Assert.Contains("Spend a little more", result.CodeError!);
    }

    [Fact]
    public async Task A_promotion_targeted_at_another_brand_does_not_apply()
    {
        var code = $"ZZBRD{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        await AddPromotionAsync(new Promotion
        {
            Name = "ZZ-other-brand", Code = code,
            Kind = PromotionKinds.PercentOff, Value = 5000,
            BrandIds = "some-other-brand-id", IsActive = true,
        });

        var result = await EvaluateAsync(Basket(), code);

        Assert.DoesNotContain(result.Applied, a => a.Code == code);
        Assert.Contains("doesn't apply", result.CodeError!);
    }

    [Fact]
    public async Task An_empty_basket_yields_no_promotions_rather_than_a_free_discount()
    {
        var result = await EvaluateAsync([]);

        Assert.Empty(result.Applied);
        Assert.Equal(0, result.DiscountMinor);
    }

    [Fact]
    public async Task The_seeded_welcome_code_matches_the_figure_the_legacy_shop_produced()
    {
        // Verified in the running Next.js application before migration:
        // a Rs.6,500 frame with WELCOME15 discounts by exactly Rs.975.
        var result = await EvaluateAsync(Basket(unitMinor: 650000, qty: 1), "WELCOME15");

        var welcome = result.Applied.FirstOrDefault(a => a.Code == "WELCOME15");
        Assert.NotNull(welcome);
        Assert.Equal(97500, welcome!.DiscountMinor);
        Assert.Equal(Money.ApplyBps(650000, 1500), welcome.DiscountMinor);
    }
}
