using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Common;
using VisionCart.Application.Platform;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;
using VisionCart.Domain.ValueObjects;

namespace VisionCart.Application.Admin;

public sealed class PromotionDetails
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Code { get; set; }
    public string Kind { get; set; } = PromotionKinds.PercentOff;

    /// <summary>Percent for percent_off; major-unit money for amount_off and bundle.</summary>
    public decimal Value { get; set; }

    public decimal? MaxDiscount { get; set; }
    public decimal MinSubtotal { get; set; }
    public int MinQty { get; set; } = 1;
    public string? BrandIds { get; set; }
    public string? CategoryIds { get; set; }
    public bool FirstOrderOnly { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public int? UsageLimit { get; set; }
    public int? UsageLimitPerUser { get; set; }
    public bool Stackable { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
    public string? BannerText { get; set; }
}

public sealed class ShippingRateDetails
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string Country { get; set; } = "PK";
    public string? Region { get; set; }
    public decimal MinSubtotal { get; set; }
    public decimal? MaxSubtotal { get; set; }
    public decimal Price { get; set; }
    public int EtaDaysMin { get; set; } = 2;
    public int EtaDaysMax { get; set; } = 5;
    public string? Carrier { get; set; }
    public bool IsActive { get; set; } = true;
    public int Position { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}

public sealed class AuditFilters
{
    public string? Q { get; init; }
    public string? Action { get; init; }
    public string? Entity { get; init; }
    public string? UserId { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int Page { get; init; } = 1;
    public int PerPage { get; init; } = 50;
}

public sealed class AuditRow
{
    public string Id { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Entity { get; init; } = string.Empty;
    public string? EntityId { get; init; }
    public string? Actor { get; init; }
    public string? Ip { get; init; }
    public string? Detail { get; init; }
}

public interface IPlatformAdminService
{
    Task<IReadOnlyList<Promotion>> ListPromotionsAsync(CancellationToken ct = default);
    Task<Promotion?> GetPromotionAsync(string id, CancellationToken ct = default);
    Task<ActionResult<string>> SavePromotionAsync(string? id, PromotionDetails details, CancellationToken ct = default);
    Task<ActionResult> TogglePromotionAsync(string id, CancellationToken ct = default);

    Task<IReadOnlyList<ShippingRate>> ListShippingRatesAsync(CancellationToken ct = default);
    Task<ShippingRate?> GetShippingRateAsync(string id, CancellationToken ct = default);
    Task<ActionResult> SaveShippingRateAsync(string? id, ShippingRateDetails details, CancellationToken ct = default);
    Task<ActionResult> ToggleShippingRateAsync(string id, CancellationToken ct = default);

    Task<PagedResult<AuditRow>> ListAuditAsync(AuditFilters filters, CancellationToken ct = default);
    Task<IReadOnlyList<string>> AuditActionNamesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> AuditEntityNamesAsync(CancellationToken ct = default);

    Task<ActionResult> SaveSettingsAsync(IReadOnlyDictionary<string, string> values,
        IReadOnlyList<string> booleanKeys, CancellationToken ct = default);
}

/// <summary>
/// Promotions, delivery rates, settings and the audit viewer.
///
/// Two of these did not exist in the legacy application: delivery prices lived
/// in a table only the code read, and the audit trail was written in 26 places
/// and read by nothing.
/// </summary>
public sealed class PlatformAdminService(
    IApplicationDbContext db,
    ISettingsService settings,
    IAuditService audit,
    Microsoft.Extensions.Options.IOptions<Pricing.StoreOptions> store) : IPlatformAdminService
{
    private readonly string _currency = store.Value.Currency;

    // --- Promotions ---------------------------------------------------------

    public async Task<IReadOnlyList<Promotion>> ListPromotionsAsync(CancellationToken ct = default) =>
        await db.Promotions.AsNoTracking()
            .OrderByDescending(p => p.Priority).ThenBy(p => p.Name)
            .ToListAsync(ct);

    public async Task<Promotion?> GetPromotionAsync(string id, CancellationToken ct = default) =>
        await db.Promotions.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<ActionResult<string>> SavePromotionAsync(
        string? id, PromotionDetails d, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return ActionResult<string>.Fail("Give the deal a name.");
        if (!PromotionKinds.All.Contains(d.Kind)) return ActionResult<string>.Fail("Choose a deal type.");

        var promotion = id is null
            ? new Promotion()
            : await db.Promotions.FirstOrDefaultAsync(p => p.Id == id, ct);

        if (promotion is null) return ActionResult<string>.Fail("That deal no longer exists.");

        promotion.Name = d.Name.Trim();
        promotion.Description = Blank(d.Description);
        promotion.Code = string.IsNullOrWhiteSpace(d.Code) ? null : d.Code.Trim().ToUpperInvariant();
        promotion.Kind = d.Kind;

        // Percent kinds store basis points; money kinds store minor units. Both
        // arrive from the same form field, so the conversion depends on the kind.
        promotion.Value = d.Kind switch
        {
            PromotionKinds.PercentOff => (int)Math.Round(d.Value * 100),
            PromotionKinds.AmountOff or PromotionKinds.Bundle => Money.ToMinor(d.Value, _currency),
            _ => 0,
        };

        promotion.MaxDiscountMinor = d.MaxDiscount is { } m ? Money.ToMinor(m, _currency) : null;
        promotion.MinSubtotalMinor = Money.ToMinor(d.MinSubtotal, _currency);
        promotion.MinQty = Math.Max(1, d.MinQty);
        promotion.BrandIds = Blank(d.BrandIds);
        promotion.CategoryIds = Blank(d.CategoryIds);
        promotion.FirstOrderOnly = d.FirstOrderOnly;
        promotion.StartsAt = d.StartsAt;
        promotion.EndsAt = d.EndsAt;
        promotion.UsageLimit = d.UsageLimit;
        promotion.UsageLimitPerUser = d.UsageLimitPerUser;
        promotion.Stackable = d.Stackable;
        promotion.Priority = d.Priority;
        promotion.IsActive = d.IsActive;
        promotion.BannerText = Blank(d.BannerText);

        if (id is null) db.Promotions.Add(promotion);

        if (promotion.Code is not null)
        {
            var clash = await db.Promotions.AnyAsync(
                p => p.Id != promotion.Id && p.Code == promotion.Code, ct);
            if (clash) return ActionResult<string>.Fail("Another deal already uses that code.");
        }

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.PriceUpdate, "Promotion", promotion.Id, new
        {
            name = promotion.Name,
            code = promotion.Code,
            kind = promotion.Kind,
            isActive = promotion.IsActive,
        }, ct);

        return ActionResult<string>.Success(promotion.Id);
    }

    public async Task<ActionResult> TogglePromotionAsync(string id, CancellationToken ct = default)
    {
        var promotion = await db.Promotions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (promotion is null) return ActionResult.Fail("That deal no longer exists.");

        promotion.IsActive = !promotion.IsActive;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.PriceUpdate, "Promotion", promotion.Id,
            new { name = promotion.Name, isActive = promotion.IsActive }, ct);

        return ActionResult.Success();
    }

    // --- Delivery rates -----------------------------------------------------

    public async Task<IReadOnlyList<ShippingRate>> ListShippingRatesAsync(CancellationToken ct = default) =>
        await db.ShippingRates.AsNoTracking()
            .OrderBy(r => r.Country).ThenBy(r => r.Position).ThenBy(r => r.PriceMinor)
            .ToListAsync(ct);

    public async Task<ShippingRate?> GetShippingRateAsync(string id, CancellationToken ct = default) =>
        await db.ShippingRates.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<ActionResult> SaveShippingRateAsync(
        string? id, ShippingRateDetails d, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return ActionResult.Fail("Give the delivery option a name.");
        if (d.Country.Length != 2) return ActionResult.Fail("Country must be a two-letter code.");
        if (d.EtaDaysMin < 0 || d.EtaDaysMax < d.EtaDaysMin)
            return ActionResult.Fail("Check the delivery estimate — the maximum must not be below the minimum.");

        var rate = id is null
            ? new ShippingRate()
            : await db.ShippingRates.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (rate is null) return ActionResult.Fail("That delivery rate no longer exists.");

        rate.Name = d.Name.Trim();
        rate.Code = Blank(d.Code) ?? Slugify(d.Name);
        rate.Country = d.Country.ToUpperInvariant();
        rate.Region = Blank(d.Region);
        rate.MinSubtotalMinor = Money.ToMinor(d.MinSubtotal, _currency);
        rate.MaxSubtotalMinor = d.MaxSubtotal is { } max ? Money.ToMinor(max, _currency) : null;
        rate.PriceMinor = Money.ToMinor(d.Price, _currency);
        rate.EtaDaysMin = d.EtaDaysMin;
        rate.EtaDaysMax = d.EtaDaysMax;
        rate.Carrier = Blank(d.Carrier);
        rate.IsActive = d.IsActive;
        rate.Position = d.Position;
        rate.EffectiveFrom = d.EffectiveFrom;
        rate.EffectiveTo = d.EffectiveTo;

        if (id is null) db.ShippingRates.Add(rate);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.PriceUpdate, "ShippingRate", rate.Id, new
        {
            name = rate.Name,
            country = rate.Country,
            priceMinor = rate.PriceMinor,
        }, ct);

        return ActionResult.Success();
    }

    public async Task<ActionResult> ToggleShippingRateAsync(string id, CancellationToken ct = default)
    {
        var rate = await db.ShippingRates.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rate is null) return ActionResult.Fail("That delivery rate no longer exists.");

        rate.IsActive = !rate.IsActive;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.PriceUpdate, "ShippingRate", rate.Id,
            new { name = rate.Name, isActive = rate.IsActive }, ct);

        return ActionResult.Success();
    }

    // --- Audit viewer -------------------------------------------------------

    public async Task<PagedResult<AuditRow>> ListAuditAsync(
        AuditFilters filters, CancellationToken ct = default)
    {
        var query = db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filters.Q))
        {
            var term = filters.Q.Trim();
            query = query.Where(a =>
                (a.EntityId != null && EF.Functions.Like(a.EntityId, $"%{term}%"))
                || (a.ActorEmail != null && EF.Functions.Like(a.ActorEmail, $"%{term}%"))
                || (a.Ip != null && EF.Functions.Like(a.Ip, $"%{term}%")));
        }

        if (!string.IsNullOrEmpty(filters.Action)) query = query.Where(a => a.Action == filters.Action);
        if (!string.IsNullOrEmpty(filters.Entity)) query = query.Where(a => a.Entity == filters.Entity);
        if (!string.IsNullOrEmpty(filters.UserId)) query = query.Where(a => a.UserId == filters.UserId);
        if (filters.From is { } from) query = query.Where(a => a.CreatedAt >= from);
        if (filters.To is { } to) query = query.Where(a => a.CreatedAt <= to);

        var total = await query.CountAsync(ct);
        var perPage = Math.Clamp(filters.PerPage, 1, 200);
        var page = Math.Max(1, filters.Page);

        var rows = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(a => new AuditRow
            {
                Id = a.Id,
                CreatedAt = a.CreatedAt,
                Action = a.Action,
                Entity = a.Entity,
                EntityId = a.EntityId,
                // ActorEmail is denormalised onto the row so an entry still reads
                // correctly after a staff account is removed.
                Actor = a.ActorEmail ?? (a.User == null ? null : a.User.Email),
                Ip = a.Ip,
                Detail = a.Detail,
            })
            .ToListAsync(ct);

        return new PagedResult<AuditRow> { Items = rows, Total = total, Page = page, PerPage = perPage };
    }

    public async Task<IReadOnlyList<string>> AuditActionNamesAsync(CancellationToken ct = default) =>
        await db.AuditLogs.AsNoTracking().Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(ct);

    public async Task<IReadOnlyList<string>> AuditEntityNamesAsync(CancellationToken ct = default) =>
        await db.AuditLogs.AsNoTracking().Select(a => a.Entity).Distinct().OrderBy(a => a).ToListAsync(ct);

    // --- Settings -----------------------------------------------------------

    public async Task<ActionResult> SaveSettingsAsync(
        IReadOnlyDictionary<string, string> values, IReadOnlyList<string> booleanKeys,
        CancellationToken ct = default)
    {
        var changed = new List<string>();

        foreach (var (key, value) in values)
        {
            // A ticked checkbox posts "on"; store the boolean the readers expect.
            var stored = value == "on" ? "true" : value;
            await settings.SetAsync(key, stored, key.Split('.')[0], ct);
            changed.Add(key);
        }

        // Unchecked checkboxes are absent from the form, so booleans need an
        // explicit list of which ones the page rendered.
        foreach (var key in booleanKeys.Where(k => !values.ContainsKey(k)))
        {
            await settings.SetAsync(key, "false", key.Split('.')[0], ct);
            changed.Add(key);
        }

        await audit.WriteAsync(AuditActions.SettingsUpdate, "Setting", null, new { keys = changed }, ct);
        return ActionResult.Success();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Slugify(string value)
    {
        var slug = new string([.. value.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]);
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}
