using VisionCart.Domain.Constants;
using VisionCart.Domain.ValueObjects;

namespace VisionCart.Domain.Entities;

public class Cart
{
    public string Id { get; set; } = Cuid.NewId();
    public string Token { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public string Currency { get; set; } = "PKR";
    public string? PromoCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CartItem> Items { get; set; } = [];
}

public class CartItem
{
    public string Id { get; set; } = Cuid.NewId();
    public string CartId { get; set; } = string.Empty;
    public Cart Cart { get; set; } = null!;
    public string VariantId { get; set; } = string.Empty;
    public FrameVariant Variant { get; set; } = null!;
    public int Qty { get; set; } = 1;

    /// <summary>Chosen lens option codes, comma separated; resolved at price time.</summary>
    public string? LensOptionCodes { get; set; }

    /// <summary>Rx captured during checkout before a Patient record exists, JSON string.</summary>
    public string? PrescriptionDraft { get; set; }

    public string? PrescriptionId { get; set; }
    public string? TryOnSnapshotId { get; set; }

    // Cached only — never trusted. Every read re-prices from the database.
    public int UnitPriceMinor { get; set; }
    public int LensPriceMinor { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Order
{
    public string Id { get; set; } = Cuid.NewId();
    public string OrderNo { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public string? PatientId { get; set; }
    public Patient? Patient { get; set; }

    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }

    /// <summary>pending | paid | in_lab | ready | shipped | delivered | cancelled | refunded</summary>
    public string Status { get; set; } = OrderStatuses.Pending;

    /// <summary>unpaid | authorized | paid | partially_refunded | refunded | failed</summary>
    public string PaymentStatus { get; set; } = PaymentStatuses.Unpaid;

    /// <summary>unfulfilled | lab_processing | quality_check | packed | shipped | delivered</summary>
    public string FulfilmentStatus { get; set; } = FulfilmentStatuses.Unfulfilled;

    public string Currency { get; set; } = "PKR";
    public int SubtotalMinor { get; set; }
    public int LensTotalMinor { get; set; }
    public int DiscountMinor { get; set; }
    public int ShippingMinor { get; set; }
    public int TaxMinor { get; set; }
    public int TotalMinor { get; set; }

    public string? PromoCode { get; set; }
    public string? PromotionId { get; set; }
    public Promotion? Promotion { get; set; }

    public string? ShippingAddressId { get; set; }
    public Address? ShippingAddress { get; set; }
    public string? BillingAddressId { get; set; }
    public Address? BillingAddress { get; set; }

    public string? Notes { get; set; }
    public string? InternalNotes { get; set; }

    public DateTime PlacedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public ICollection<OrderItem> Items { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
    public ICollection<Shipment> Shipments { get; set; } = [];
}

public class OrderItem
{
    public string Id { get; set; } = Cuid.NewId();
    public string OrderId { get; set; } = string.Empty;
    public Order Order { get; set; } = null!;
    public string? VariantId { get; set; }
    public FrameVariant? Variant { get; set; }

    // Denormalised so the invoice still reads correctly if the catalogue changes
    public string TitleSnapshot { get; set; } = string.Empty;
    public string SkuSnapshot { get; set; } = string.Empty;
    public string? ImageSnapshot { get; set; }

    public int Qty { get; set; } = 1;
    public int UnitPriceMinor { get; set; }
    public int LensPriceMinor { get; set; }
    public int TotalMinor { get; set; }

    public string? LensOptionCodes { get; set; }
    public string? LensSummary { get; set; }

    public string? PrescriptionId { get; set; }
    public Prescription? Prescription { get; set; }

    /// <summary>Full JSON copy of the Rx as it was at purchase time.</summary>
    public string? PrescriptionSnapshot { get; set; }

    public string? TryOnSnapshotUrl { get; set; }

    /// <summary>pending | ordered | surfacing | coating | glazing | qc | ready</summary>
    public string LabStatus { get; set; } = LabStatuses.Pending;

    public string? LabRef { get; set; }
}

public class Payment
{
    public string Id { get; set; } = Cuid.NewId();
    public string OrderId { get; set; } = string.Empty;
    public Order Order { get; set; } = null!;

    /// <summary>stripe | cod | bank_transfer | manual</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>pending | authorized | succeeded | failed | refunded</summary>
    public string Status { get; set; } = "pending";

    public int AmountMinor { get; set; }
    public string Currency { get; set; } = "PKR";
    public string? ProviderRef { get; set; }

    /// <summary>Raw provider payload for reconciliation, JSON string.</summary>
    public string? RawPayload { get; set; }

    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Added during migration. The legacy webhook had no replay protection: a
    /// provider redelivery could mark an order paid twice and double-count
    /// revenue. This holds the provider's event id and is uniquely indexed, so
    /// a repeated delivery is rejected by the database rather than by a race.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    public int RefundedMinor { get; set; }
}

public class Shipment
{
    public string Id { get; set; } = Cuid.NewId();
    public string OrderId { get; set; } = string.Empty;
    public Order Order { get; set; } = null!;

    /// <summary>tcs | leopards | dhl | fedex | ups | local | other</summary>
    public string Carrier { get; set; } = string.Empty;

    public string? Service { get; set; }
    public string? TrackingNumber { get; set; }
    public string? TrackingUrl { get; set; }
    public string? LabelUrl { get; set; }
    public int CostMinor { get; set; }

    /// <summary>pending | label_created | in_transit | out_for_delivery | delivered | returned</summary>
    public string Status { get; set; } = ShipmentStatuses.Pending;

    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? ProviderRef { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ShippingRate
{
    public string Id { get; set; } = Cuid.NewId();
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = "PK";
    public string? Region { get; set; }

    /// <summary>Applies when the order subtotal falls inside this window.</summary>
    public int MinSubtotalMinor { get; set; }
    public int? MaxSubtotalMinor { get; set; }

    public int PriceMinor { get; set; }
    public int EtaDaysMin { get; set; } = 2;
    public int EtaDaysMax { get; set; } = 5;
    public string? Carrier { get; set; }
    public bool IsActive { get; set; } = true;
    public int Position { get; set; }

    // --- Added during migration -------------------------------------------
    // The legacy table had no effective-date window, so a seasonal or announced
    // price change had to be made by editing the live row at the right moment.

    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }

    /// <summary>Stable code shown at checkout and stored on the shipment.</summary>
    public string? Code { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Promotion
{
    public string Id { get; set; } = Cuid.NewId();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Null = automatic promotion applied without a code.</summary>
    public string? Code { get; set; }

    /// <summary>percent_off | amount_off | free_shipping | bogo | free_lens_upgrade | bundle</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Percent as basis points (1500 = 15%) or minor units for amount_off.</summary>
    public int Value { get; set; }

    public int? MaxDiscountMinor { get; set; }

    // Conditions
    public int MinSubtotalMinor { get; set; }
    public int MinQty { get; set; } = 1;

    /// <summary>Comma-separated ids; empty = whole catalogue.</summary>
    public string? BrandIds { get; set; }
    public string? CategoryIds { get; set; }
    public string? FrameIds { get; set; }

    /// <summary>Restrict to first-time buyers.</summary>
    public bool FirstOrderOnly { get; set; }

    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public int? UsageLimit { get; set; }
    public int? UsageLimitPerUser { get; set; }
    public int UsageCount { get; set; }
    public bool Stackable { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Storefront banner copy, so marketing can ship a deal without a deploy.</summary>
    public string? BannerText { get; set; }
    public string? BannerColor { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Order> Orders { get; set; } = [];
}
