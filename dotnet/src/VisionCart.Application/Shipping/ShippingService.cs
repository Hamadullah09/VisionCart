using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisionCart.Application.Common;
using VisionCart.Application.Platform;

namespace VisionCart.Application.Shipping;

public sealed class ShippingQuote
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Carrier { get; init; } = "local";
    public int PriceMinor { get; init; }
    public int EtaDaysMin { get; init; }
    public int EtaDaysMax { get; init; }
    public string Provider { get; init; } = "table_rate";
    /// <summary>Provider rate id, needed later to buy the label.</summary>
    public string? RateRef { get; init; }

    public string EtaText => EtaDaysMin == EtaDaysMax
        ? $"Arrives in {EtaDaysMin} working day{(EtaDaysMin == 1 ? "" : "s")}"
        : $"Arrives in {EtaDaysMin}–{EtaDaysMax} working days";
}

public sealed class ShipAddress
{
    public string FullName { get; init; } = string.Empty;
    public string Line1 { get; init; } = string.Empty;
    public string? Line2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string Country { get; init; } = "PK";
    public string? Phone { get; init; }
    public string? Email { get; init; }
}

public sealed class ShippingQuoteRequest
{
    public int SubtotalMinor { get; init; }
    public string Country { get; init; } = "PK";
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public int ItemCount { get; init; } = 1;
    public ShipAddress? Address { get; init; }
}

public sealed class PurchasedLabel
{
    public string? TrackingNumber { get; init; }
    public string? TrackingUrl { get; init; }
    public string? LabelUrl { get; init; }
    public string? ProviderRef { get; init; }
}

/// <summary>
/// A shipping carrier integration. Implementations live in Infrastructure
/// because they make outbound HTTP calls; the rate table needs only the database
/// and stays here.
/// </summary>
public interface IShippingProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<IReadOnlyList<ShippingQuote>> GetRatesAsync(ShipAddress to, int itemCount, CancellationToken ct = default);
    Task<PurchasedLabel?> BuyLabelAsync(string rateRef, CancellationToken ct = default);
}

public sealed class ShippingOptions
{
    public const string SectionName = "Shipping";
    /// <summary>table_rate | shippo | easypost</summary>
    public string Provider { get; set; } = "table_rate";
    public string? ApiKey { get; set; }

    public string FromName { get; set; } = "Optical Store";
    public string FromLine1 { get; set; } = "";
    public string FromCity { get; set; } = "";
    public string FromState { get; set; } = "";
    public string FromPostal { get; set; } = "";
    public string FromCountry { get; set; } = "PK";
    public string FromPhone { get; set; } = "";

    public ShipAddress ShipFrom() => new()
    {
        FullName = FromName, Line1 = FromLine1, City = FromCity,
        State = FromState, PostalCode = FromPostal, Country = FromCountry, Phone = FromPhone,
    };
}

public interface IShippingService
{
    Task<IReadOnlyList<ShippingQuote>> QuoteAsync(ShippingQuoteRequest request, CancellationToken ct = default);
    Task<PurchasedLabel?> BuyLabelAsync(string? rateRef, CancellationToken ct = default);
}

/// <summary>
/// Port of <c>src/lib/shipping.ts</c>.
///
/// The rate table is the default and needs no account. A live carrier provider,
/// when configured, is tried first and <b>falls back to the rate table</b> if its
/// key is missing or the call fails — so a carrier outage degrades to flat-rate
/// delivery instead of blocking checkout. That behaviour is the whole point of
/// this class and is covered by tests.
/// </summary>
public sealed class ShippingService(
    IApplicationDbContext db,
    ISettingsService settings,
    IEnumerable<IShippingProvider> providers,
    IOptions<ShippingOptions> options,
    TimeProvider clock,
    ILogger<ShippingService> logger) : IShippingService
{
    private readonly ShippingOptions _options = options.Value;

    public async Task<IReadOnlyList<ShippingQuote>> QuoteAsync(
        ShippingQuoteRequest request, CancellationToken ct = default)
    {
        var provider = providers.FirstOrDefault(p =>
            string.Equals(p.Name, _options.Provider, StringComparison.OrdinalIgnoreCase));

        if (provider is { IsConfigured: true } && request.Address is not null)
        {
            try
            {
                var live = await provider.GetRatesAsync(request.Address, request.ItemCount, ct);
                if (live.Count > 0) return live;

                logger.LogWarning("Shipping provider {Provider} returned no rates; using the rate table",
                    provider.Name);
            }
            catch (Exception ex)
            {
                // Deliberately swallowed. A carrier being down must not stop a
                // customer from buying glasses.
                logger.LogError(ex, "Shipping provider {Provider} failed; falling back to the rate table",
                    provider.Name);
            }
        }

        return await TableRatesAsync(request, ct);
    }

    public async Task<PurchasedLabel?> BuyLabelAsync(string? rateRef, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rateRef)) return null;

        var provider = providers.FirstOrDefault(p =>
            string.Equals(p.Name, _options.Provider, StringComparison.OrdinalIgnoreCase));

        if (provider is not { IsConfigured: true }) return null;

        try
        {
            return await provider.BuyLabelAsync(rateRef, ct);
        }
        catch (Exception ex)
        {
            // Staff can still record courier details by hand from the order page.
            logger.LogError(ex, "Could not buy a label from {Provider}", provider.Name);
            return null;
        }
    }

    private async Task<IReadOnlyList<ShippingQuote>> TableRatesAsync(
        ShippingQuoteRequest request, CancellationToken ct)
    {
        var freeOver = await settings.GetIntAsync(SettingKeys.FreeShippingOverMinor, 0, ct);
        var country = request.Country.ToUpperInvariant();
        var now = clock.GetUtcNow().UtcDateTime;

        var rows = await db.ShippingRates
            .AsNoTracking()
            .Where(r => r.IsActive
                        && r.Country == country
                        // Effective-date window added during migration so a price
                        // change can be scheduled instead of made by hand at the
                        // moment it should take effect.
                        && (r.EffectiveFrom == null || r.EffectiveFrom <= now)
                        && (r.EffectiveTo == null || r.EffectiveTo >= now))
            .OrderBy(r => r.Position).ThenBy(r => r.PriceMinor)
            .ToListAsync(ct);

        var matching = rows.Where(r =>
            request.SubtotalMinor >= r.MinSubtotalMinor
            && (r.MaxSubtotalMinor is null || request.SubtotalMinor <= r.MaxSubtotalMinor)
            && (string.IsNullOrEmpty(r.Region)
                || string.IsNullOrEmpty(request.State)
                || string.Equals(r.Region, request.State, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var source = matching.Count > 0 ? matching : rows;

        if (source.Count == 0)
        {
            // Nothing configured for this country yet — quote a single free line
            // so a fresh install can still take an order.
            return
            [
                new ShippingQuote
                {
                    Code = "standard",
                    Name = "Standard delivery",
                    Carrier = "local",
                    PriceMinor = 0,
                    EtaDaysMin = 3,
                    EtaDaysMax = 7,
                    Provider = "table_rate",
                },
            ];
        }

        return
        [
            .. source.Select(r => new ShippingQuote
            {
                Code = r.Code ?? r.Id,
                Name = r.Name,
                Carrier = r.Carrier ?? "local",
                PriceMinor = freeOver > 0 && request.SubtotalMinor >= freeOver ? 0 : r.PriceMinor,
                EtaDaysMin = r.EtaDaysMin,
                EtaDaysMax = r.EtaDaysMax,
                Provider = "table_rate",
            }),
        ];
    }
}
