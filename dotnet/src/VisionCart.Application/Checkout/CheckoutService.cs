using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VisionCart.Application.Carts;
using VisionCart.Application.Common;
using VisionCart.Application.Patients;
using VisionCart.Application.Payments;
using VisionCart.Application.Platform;
using VisionCart.Application.Prescriptions;
using VisionCart.Application.Pricing;
using VisionCart.Application.Promotions;
using VisionCart.Application.Shipping;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;

namespace VisionCart.Application.Checkout;

public sealed class CheckoutInput
{
    [Required(ErrorMessage = "Enter a valid email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a phone number we can reach you on.")]
    [StringLength(30, MinimumLength = 6, ErrorMessage = "Enter a phone number we can reach you on.")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the name for delivery.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Enter the name for delivery.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the street address.")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "Enter the street address.")]
    public string Line1 { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Line2 { get; set; }

    [Required(ErrorMessage = "Enter the city.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Enter the city.")]
    public string City { get; set; } = string.Empty;

    [StringLength(100)] public string? State { get; set; }
    [StringLength(20)] public string? PostalCode { get; set; }
    [StringLength(2, MinimumLength = 2)] public string Country { get; set; } = "PK";

    public string? ShippingCode { get; set; }

    [Required(ErrorMessage = "Choose how you'd like to pay.")]
    public string PaymentMethod { get; set; } = string.Empty;

    [StringLength(1000)] public string? Notes { get; set; }
    public bool SaveAddress { get; set; }
}

public sealed class PlaceOrderOutcome
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public string? OrderNo { get; init; }
    /// <summary>Where to send the customer next — the payment page or the order page.</summary>
    public string? RedirectUrl { get; init; }

    public static PlaceOrderOutcome Fail(string error) => new() { Ok = false, Error = error };
}

public interface ICheckoutService
{
    Task<PlaceOrderOutcome> PlaceOrderAsync(CheckoutInput input, CancellationToken ct = default);
    Task<IReadOnlyList<ShippingQuote>> ShippingQuotesAsync(string country, string? state, CancellationToken ct = default);
}

/// <summary>
/// Port of <c>placeOrderAction</c> in <c>src/app/actions/checkout.ts</c>.
///
/// The most complex path in the system. Every design decision in the project
/// shows up here: the cart is re-priced from the database and nothing the
/// browser sent about price is trusted; a prescription typed at checkout becomes
/// a real versioned record marked pending verification; each order line takes its
/// own frozen snapshot; and a payment provider failing does not lose the sale.
/// </summary>
public sealed class CheckoutService(
    IApplicationDbContext db,
    ICartService carts,
    IPricingService pricing,
    IPromotionService promotions,
    IShippingService shipping,
    IPaymentService payments,
    IPatientService patients,
    ISettingsService settings,
    IAuditService audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<CheckoutService> logger) : ICheckoutService
{
    public async Task<IReadOnlyList<ShippingQuote>> ShippingQuotesAsync(
        string country, string? state, CancellationToken ct = default)
    {
        var cart = await carts.PeekAsync(ct);
        if (cart is null) return [];

        var view = await carts.BuildViewAsync(cart.Id, country, ct);
        var goods = view.Lines.Sum(l => l.TotalMinor);

        return await shipping.QuoteAsync(new ShippingQuoteRequest
        {
            SubtotalMinor = goods - view.Totals.DiscountMinor,
            Country = country,
            State = state,
            ItemCount = view.ItemCount,
        }, ct);
    }

    public async Task<PlaceOrderOutcome> PlaceOrderAsync(CheckoutInput input, CancellationToken ct = default)
    {
        if (!payments.EnabledMethods().Any(m => m.Id == input.PaymentMethod))
            return PlaceOrderOutcome.Fail("That payment method isn't available.");

        if (!currentUser.IsAuthenticated && !await settings.GetBoolAsync(SettingKeys.CheckoutGuestAllowed, ct))
            return PlaceOrderOutcome.Fail("Please sign in or create an account to complete your order.");

        // --- Re-price everything from the database --------------------------
        var cart = await carts.PeekAsync(ct);
        if (cart is null) return PlaceOrderOutcome.Fail("Your bag has expired. Please add your frames again.");

        var view = await carts.BuildViewAsync(cart.Id, input.Country, ct);
        if (view.Lines.Count == 0) return PlaceOrderOutcome.Fail("Your bag is empty.");

        var blocking = view.Warnings.FirstOrDefault(w => w.Contains("out of stock", StringComparison.OrdinalIgnoreCase));
        if (blocking is not null) return PlaceOrderOutcome.Fail(blocking);

        // Some practices won't take an order they can't dispense.
        if (await settings.GetBoolAsync(SettingKeys.CheckoutRequirePrescription, ct))
        {
            var missing = await db.CartItems.AnyAsync(
                i => i.CartId == cart.Id && i.PrescriptionId == null && i.PrescriptionDraft == null, ct);

            if (missing)
            {
                return PlaceOrderOutcome.Fail(
                    "This store needs a prescription on every pair before checkout. " +
                    "Add yours to each item in your bag.");
            }
        }

        var promo = await promotions.EvaluateAsync(
            view.Lines, cart.PromoCode, currentUser.UserId, input.Email, ct);

        var goods = view.Lines.Sum(l => l.TotalMinor);

        var quotes = await shipping.QuoteAsync(new ShippingQuoteRequest
        {
            SubtotalMinor = goods - promo.DiscountMinor,
            Country = input.Country,
            State = input.State,
            PostalCode = input.PostalCode,
            ItemCount = view.Lines.Sum(l => l.Qty),
            Address = new ShipAddress
            {
                FullName = input.FullName,
                Line1 = input.Line1,
                Line2 = input.Line2,
                City = input.City,
                State = input.State,
                PostalCode = input.PostalCode,
                Country = input.Country,
                Phone = input.Phone,
                Email = input.Email,
            },
        }, ct);

        var chosenQuote = quotes.FirstOrDefault(q => q.Code == input.ShippingCode) ?? quotes.FirstOrDefault();
        var shippingMinor = promo.FreeShipping ? 0 : chosenQuote?.PriceMinor ?? 0;

        var totals = pricing.ComputeTotals(view.Lines, promo.DiscountMinor, shippingMinor, cart.Currency);

        // --- Patient file ---------------------------------------------------
        var patient = currentUser.IsAuthenticated
            ? await patients.EnsureForUserAsync(currentUser.UserId!, ct)
            : await patients.FindOrCreateGuestAsync(input.Email, input.Phone, input.FullName, ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var patientId = patient.Id;

        string orderNo = string.Empty;
        string orderId = string.Empty;

        // --- Write the order, atomically ------------------------------------
        // Everything inside runs as one retriable unit. The delegate re-reads the
        // patient and the cart rather than reusing entities tracked before it,
        // because a transient-failure retry starts with a cleared change tracker.
        await db.ExecuteInTransactionAsync(async token =>
        {
            var trackedPatient = await db.Patients.FirstAsync(p => p.Id == patientId, token);
            orderNo = await NextOrderNoAsync(token);

            var address = new Address
            {
                UserId = currentUser.UserId,
                FullName = input.FullName,
                Phone = input.Phone,
                Line1 = input.Line1,
                Line2 = string.IsNullOrWhiteSpace(input.Line2) ? null : input.Line2,
                City = input.City,
                State = input.State,
                PostalCode = input.PostalCode,
                Country = input.Country.ToUpperInvariant(),
                IsDefault = input.SaveAddress,
            };
            db.Addresses.Add(address);

            var order = new Order
            {
                OrderNo = orderNo,
                UserId = currentUser.UserId,
                PatientId = trackedPatient.Id,
                Email = input.Email.ToLowerInvariant(),
                Phone = input.Phone,
                Status = OrderStatuses.Pending,
                PaymentStatus = PaymentStatuses.Unpaid,
                Currency = totals.Currency,
                SubtotalMinor = totals.SubtotalMinor,
                LensTotalMinor = totals.LensTotalMinor,
                DiscountMinor = totals.DiscountMinor,
                ShippingMinor = totals.ShippingMinor,
                TaxMinor = totals.TaxMinor,
                TotalMinor = totals.TotalMinor,
                PromoCode = cart.PromoCode,
                PromotionId = promo.Applied.FirstOrDefault()?.Id,
                ShippingAddress = address,
                BillingAddress = address,
                Notes = input.Notes,
                PlacedAt = now,
            };
            db.Orders.Add(order);

            var cartItems = await db.CartItems.Where(i => i.CartId == cart.Id).ToListAsync(token);

            foreach (var line in view.Lines)
            {
                var cartItem = cartItems.FirstOrDefault(i => i.Id == line.ItemId);
                var draft = ICartService.ParseRxDraft(cartItem?.PrescriptionDraft);

                // A prescription typed at checkout becomes a real, versioned
                // record on the patient's file — so a repeat order can reuse it
                // and the optician has something to verify.
                var prescriptionId = cartItem?.PrescriptionId;

                if (prescriptionId is null && draft is not null)
                {
                    var rx = new Prescription
                    {
                        PatientId = trackedPatient.Id,
                        Source = RxSources.ManualEntry,
                        Status = RxStatuses.PendingVerification,
                        IssuedAt = now,
                        OdSphere = draft.OdSphere, OdCylinder = draft.OdCylinder, OdAxis = draft.OdAxis,
                        OdAdd = draft.OdAdd, OdPrism = draft.OdPrism, OdPrismBase = draft.OdPrismBase,
                        OdPdMm = draft.OdPdMm, OdSegHeightMm = draft.OdSegHeightMm,
                        OsSphere = draft.OsSphere, OsCylinder = draft.OsCylinder, OsAxis = draft.OsAxis,
                        OsAdd = draft.OsAdd, OsPrism = draft.OsPrism, OsPrismBase = draft.OsPrismBase,
                        OsPdMm = draft.OsPdMm, OsSegHeightMm = draft.OsSegHeightMm,
                        Prescriber = draft.Prescriber, Clinic = draft.Clinic, Notes = draft.Notes,
                    };
                    db.Prescriptions.Add(rx);
                    prescriptionId = rx.Id;

                    // The binocular PD is a property of the person, not of one
                    // prescription, so it is recorded on the file instead.
                    if (draft.PdMm is { } pd)
                    {
                        trackedPatient.PdMm = pd;
                        if (draft.PdNearMm is { } near) trackedPatient.PdNearMm = near;
                    }
                }

                var rxRecord = prescriptionId is null
                    ? null
                    : await db.Prescriptions.FirstOrDefaultAsync(p => p.Id == prescriptionId, ct)
                      ?? db.Prescriptions.Local.FirstOrDefault(p => p.Id == prescriptionId);

                db.OrderItems.Add(new OrderItem
                {
                    Order = order,
                    VariantId = line.VariantId,
                    TitleSnapshot = line.Title,
                    SkuSnapshot = line.Sku,
                    ImageSnapshot = line.ImageUrl,
                    Qty = line.Qty,
                    UnitPriceMinor = line.FramePriceMinor,
                    LensPriceMinor = line.LensPriceMinor,
                    TotalMinor = line.TotalMinor,
                    LensOptionCodes = PricingService.JoinCodes(line.LensOptions.Select(o => o.Code)) is { Length: > 0 } c
                        ? c : null,
                    LensSummary = line.LensSummary,
                    PrescriptionId = prescriptionId,
                    // The snapshot must stand alone on an invoice or a remake
                    // years later, so it carries the PD from the file alongside.
                    PrescriptionSnapshot = rxRecord is null ? null : JsonSerializer.Serialize(new
                    {
                        rx = Rx.FromEntity(rxRecord),
                        summary = Rx.Summarise(Rx.FromEntity(rxRecord)),
                        patientPdMm = trackedPatient.PdMm ?? draft?.PdMm,
                        issuedAt = rxRecord.IssuedAt,
                        status = rxRecord.Status,
                    }),
                    LabStatus = LabStatuses.Pending,
                });

                // Reserve stock at order time. Selling the last frame twice is
                // far more expensive to unpick than a brief oversell window is
                // to avoid.
                var variant = await db.FrameVariants.FirstAsync(v => v.Id == line.VariantId, token);
                variant.StockQty -= line.Qty;
            }

            // The cart is consumed; keep the row for analytics but empty the lines.
            db.CartItems.RemoveRange(cartItems);
            var trackedCart = await db.Carts.FirstAsync(c => c.Id == cart.Id, token);
            trackedCart.PromoCode = null;

            if (promo.Applied.FirstOrDefault() is { } appliedPromo)
            {
                var promotion = await db.Promotions.FirstOrDefaultAsync(p => p.Id == appliedPromo.Id, token);
                if (promotion is not null) promotion.UsageCount += 1;
            }

            await db.SaveChangesAsync(token);
            orderId = order.Id;
        }, ct);

        await audit.WriteAsync(AuditActions.OrderPlace, "Order", orderId, new
        {
            orderNo,
            totalMinor = totals.TotalMinor,
            method = input.PaymentMethod,
        }, ct);

        // --- Payment ---------------------------------------------------------
        // Re-read outside the transaction: the payment provider may redirect the
        // customer away, and the order must already be committed before it does.
        var placedOrder = await db.Orders.FirstAsync(o => o.Id == orderId, ct);
        var redirectTo = $"/order/{placedOrder.OrderNo}";

        try
        {
            var payment = await payments.StartAsync(placedOrder, input.PaymentMethod, ct);
            if (!string.IsNullOrEmpty(payment.RedirectUrl)) redirectTo = payment.RedirectUrl;
        }
        catch (Exception ex)
        {
            // The order exists and stock is held; staff can take payment manually
            // rather than losing the sale to a provider outage.
            logger.LogError(ex, "Payment could not be started for order {OrderNo}", orderNo);
            await payments.MarkPaymentFailedAsync(orderId, input.PaymentMethod, ex.Message, ct);
        }

        if (chosenQuote is not null)
        {
            db.Shipments.Add(new Shipment
            {
                OrderId = orderId,
                Carrier = chosenQuote.Carrier,
                Service = chosenQuote.Name,
                CostMinor = shippingMinor,
                Status = ShipmentStatuses.Pending,
                ProviderRef = chosenQuote.RateRef,
            });
            await db.SaveChangesAsync(ct);
        }

        carts.ClearCookie();

        return new PlaceOrderOutcome { Ok = true, OrderNo = placedOrder.OrderNo, RedirectUrl = redirectTo };
    }

    /// <summary>Sequential, human-quotable order numbers: VC-2026-000123.</summary>
    private async Task<string> NextOrderNoAsync(CancellationToken ct)
    {
        var year = clock.GetUtcNow().Year;
        var prefix = $"VC-{year}-";

        var last = await db.Orders
            .Where(o => o.OrderNo.StartsWith(prefix))
            .OrderByDescending(o => o.OrderNo)
            .Select(o => o.OrderNo)
            .FirstOrDefaultAsync(ct);

        var n = last is null ? 1 : int.Parse(last[prefix.Length..]) + 1;
        return $"{prefix}{n:D6}";
    }
}
