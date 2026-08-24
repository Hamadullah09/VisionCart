using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisionCart.Application.Shipping;
using VisionCart.Domain.ValueObjects;

namespace VisionCart.Infrastructure.Shipping;

/// <summary>A boxed pair of glasses with a case, near enough for rating purposes.</summary>
internal static class Parcel
{
    public const int LengthCm = 18;
    public const int WidthCm = 9;
    public const int HeightCm = 6;
    public const int WeightGrams = 350;
}

// --- Shippo -----------------------------------------------------------------

public sealed class ShippoShippingProvider(
    HttpClient http,
    IOptions<ShippingOptions> options,
    ILogger<ShippoShippingProvider> logger) : IShippingProvider
{
    private readonly ShippingOptions _options = options.Value;

    public string Name => "shippo";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<IReadOnlyList<ShippingQuote>> GetRatesAsync(
        ShipAddress to, int itemCount, CancellationToken ct = default)
    {
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ShippoToken", _options.ApiKey);

        var count = Math.Max(1, itemCount);

        var response = await http.PostAsJsonAsync("https://api.goshippo.com/shipments/", new
        {
            address_from = ToShippoAddress(_options.ShipFrom()),
            address_to = ToShippoAddress(to),
            parcels = new[]
            {
                new
                {
                    length = Parcel.LengthCm.ToString(CultureInfo.InvariantCulture),
                    width = Parcel.WidthCm.ToString(CultureInfo.InvariantCulture),
                    height = (Parcel.HeightCm * count).ToString(CultureInfo.InvariantCulture),
                    distance_unit = "cm",
                    weight = (Parcel.WeightGrams * count).ToString(CultureInfo.InvariantCulture),
                    mass_unit = "g",
                },
            },
            async = false,
        }, ct);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ShippoShipment>(ct);

        if (payload?.Rates is null) return [];

        return
        [
            .. payload.Rates
                .Where(r => decimal.TryParse(r.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                .Select(r => new ShippingQuote
                {
                    Code = r.ObjectId,
                    Name = r.ServiceLevel?.Name ?? r.Provider,
                    Carrier = r.Provider.ToLowerInvariant(),
                    PriceMinor = Money.ToMinor(
                        decimal.Parse(r.Amount, NumberStyles.Any, CultureInfo.InvariantCulture),
                        r.Currency),
                    EtaDaysMin = r.EstimatedDays ?? 2,
                    EtaDaysMax = (r.EstimatedDays ?? 2) + 2,
                    Provider = "shippo",
                    RateRef = r.ObjectId,
                }),
        ];
    }

    public async Task<PurchasedLabel?> BuyLabelAsync(string rateRef, CancellationToken ct = default)
    {
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ShippoToken", _options.ApiKey);

        var response = await http.PostAsJsonAsync("https://api.goshippo.com/transactions/", new
        {
            rate = rateRef,
            label_file_type = "PDF",
            async = false,
        }, ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Shippo label purchase failed with {Status}", response.StatusCode);
            return null;
        }

        var tx = await response.Content.ReadFromJsonAsync<ShippoTransaction>(ct);
        if (tx is null || !string.Equals(tx.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Shippo returned transaction status {Status}", tx?.Status);
            return null;
        }

        return new PurchasedLabel
        {
            TrackingNumber = tx.TrackingNumber,
            TrackingUrl = tx.TrackingUrlProvider,
            LabelUrl = tx.LabelUrl,
            ProviderRef = tx.ObjectId,
        };
    }

    private static object ToShippoAddress(ShipAddress a) => new
    {
        name = a.FullName,
        street1 = a.Line1,
        street2 = a.Line2 ?? "",
        city = a.City,
        state = a.State ?? "",
        zip = a.PostalCode ?? "",
        country = a.Country,
        phone = a.Phone ?? "",
        email = a.Email ?? "",
    };

    private sealed class ShippoShipment
    {
        [JsonPropertyName("rates")] public List<ShippoRate>? Rates { get; set; }
    }

    private sealed class ShippoRate
    {
        [JsonPropertyName("object_id")] public string ObjectId { get; set; } = string.Empty;
        [JsonPropertyName("amount")] public string Amount { get; set; } = "0";
        [JsonPropertyName("currency")] public string Currency { get; set; } = "USD";
        [JsonPropertyName("provider")] public string Provider { get; set; } = string.Empty;
        [JsonPropertyName("servicelevel")] public ShippoServiceLevel? ServiceLevel { get; set; }
        [JsonPropertyName("estimated_days")] public int? EstimatedDays { get; set; }
    }

    private sealed class ShippoServiceLevel
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class ShippoTransaction
    {
        [JsonPropertyName("object_id")] public string? ObjectId { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("tracking_number")] public string? TrackingNumber { get; set; }
        [JsonPropertyName("tracking_url_provider")] public string? TrackingUrlProvider { get; set; }
        [JsonPropertyName("label_url")] public string? LabelUrl { get; set; }
    }
}

// --- EasyPost ---------------------------------------------------------------

public sealed class EasyPostShippingProvider(
    HttpClient http,
    IOptions<ShippingOptions> options,
    ILogger<EasyPostShippingProvider> logger) : IShippingProvider
{
    private readonly ShippingOptions _options = options.Value;

    public string Name => "easypost";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<IReadOnlyList<ShippingQuote>> GetRatesAsync(
        ShipAddress to, int itemCount, CancellationToken ct = default)
    {
        Authorise();
        var count = Math.Max(1, itemCount);

        var response = await http.PostAsJsonAsync("https://api.easypost.com/v2/shipments", new
        {
            shipment = new
            {
                to_address = ToEasyPostAddress(to),
                from_address = ToEasyPostAddress(_options.ShipFrom()),
                parcel = new
                {
                    length = Parcel.LengthCm / 2.54,
                    width = Parcel.WidthCm / 2.54,
                    height = Parcel.HeightCm * count / 2.54,
                    weight = Parcel.WeightGrams * count / 28.3495,
                },
            },
        }, ct);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<EasyPostShipment>(ct);

        if (payload?.Rates is null) return [];

        return
        [
            .. payload.Rates
                .Where(r => decimal.TryParse(r.Rate, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                .Select(r => new ShippingQuote
                {
                    Code = r.Id,
                    Name = r.Service ?? r.Carrier,
                    Carrier = r.Carrier.ToLowerInvariant(),
                    PriceMinor = Money.ToMinor(
                        decimal.Parse(r.Rate, NumberStyles.Any, CultureInfo.InvariantCulture),
                        r.Currency),
                    EtaDaysMin = r.DeliveryDays ?? 2,
                    EtaDaysMax = (r.DeliveryDays ?? 2) + 2,
                    Provider = "easypost",
                    RateRef = r.Id,
                }),
        ];
    }

    public async Task<PurchasedLabel?> BuyLabelAsync(string rateRef, CancellationToken ct = default)
    {
        // EasyPost buys a label against the shipment, not the bare rate id. The
        // legacy implementation did not implement label purchase for EasyPost
        // either; rates fall back to the table and staff record courier details
        // by hand. Logged rather than silently returning null.
        logger.LogWarning(
            "EasyPost label purchase is not implemented; record courier details on the order instead. Rate {Rate}",
            rateRef);
        return await Task.FromResult<PurchasedLabel?>(null);
    }

    private void Authorise()
    {
        var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_options.ApiKey}:"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private static object ToEasyPostAddress(ShipAddress a) => new
    {
        name = a.FullName,
        street1 = a.Line1,
        street2 = a.Line2 ?? "",
        city = a.City,
        state = a.State ?? "",
        zip = a.PostalCode ?? "",
        country = a.Country,
        phone = a.Phone ?? "",
        email = a.Email ?? "",
    };

    private sealed class EasyPostShipment
    {
        [JsonPropertyName("rates")] public List<EasyPostRate>? Rates { get; set; }
    }

    private sealed class EasyPostRate
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("rate")] public string Rate { get; set; } = "0";
        [JsonPropertyName("currency")] public string Currency { get; set; } = "USD";
        [JsonPropertyName("carrier")] public string Carrier { get; set; } = string.Empty;
        [JsonPropertyName("service")] public string? Service { get; set; }
        [JsonPropertyName("delivery_days")] public int? DeliveryDays { get; set; }
    }
}
