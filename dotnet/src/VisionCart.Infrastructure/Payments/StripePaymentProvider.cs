using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using VisionCart.Application.Common;
using VisionCart.Application.Payments;
using VisionCart.Application.Pricing;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;

namespace VisionCart.Infrastructure.Payments;

/// <summary>
/// Hosted Stripe Checkout rather than an embedded card form: Stripe handles SCA,
/// wallets and PCI scope, and this server never touches card data.
///
/// The order is confirmed by the webhook, not by the browser coming back — a
/// customer who closes the tab after paying still gets their glasses. Preserved
/// verbatim from the legacy design.
/// </summary>
public sealed class StripePaymentProvider(
    IApplicationDbContext db,
    IOptions<PaymentOptions> paymentOptions,
    IOptions<StoreOptions> storeOptions,
    ILogger<StripePaymentProvider> logger) : IPaymentProvider
{
    private readonly PaymentOptions _payments = paymentOptions.Value;
    private readonly StoreOptions _store = storeOptions.Value;

    public string Name => PaymentProviders.Stripe;

    /// <summary>Without a key the card option is hidden rather than rendered dead.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_payments.StripeSecretKey);

    public async Task<StartPaymentResult> StartAsync(Order order, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Stripe is selected but no secret key is configured.");

        var client = new StripeClient(_payments.StripeSecretKey);
        var sessions = new SessionService(client);
        var baseUrl = _store.AppUrl.TrimEnd('/');

        var session = await sessions.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment",
            CustomerEmail = string.IsNullOrWhiteSpace(order.Email) ? null : order.Email,
            ClientReferenceId = order.Id,
            // Charged as one line: the order total is already computed
            // server-side with lenses, discounts, delivery and tax settled.
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = order.Currency.ToLowerInvariant(),
                        UnitAmount = order.TotalMinor,
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"Order {order.OrderNo}",
                        },
                    },
                },
            ],
            Metadata = new Dictionary<string, string>
            {
                ["orderId"] = order.Id,
                ["orderNo"] = order.OrderNo,
            },
            PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    ["orderId"] = order.Id,
                    ["orderNo"] = order.OrderNo,
                },
            },
            SuccessUrl = $"{baseUrl}/order/{order.OrderNo}?paid=1",
            CancelUrl = $"{baseUrl}/checkout?cancelled={order.OrderNo}",
        }, cancellationToken: ct);

        var payment = new Payment
        {
            OrderId = order.Id,
            Provider = Name,
            Status = "pending",
            AmountMinor = order.TotalMinor,
            Currency = order.Currency,
            ProviderRef = session.Id,
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        return new StartPaymentResult
        {
            PaymentId = payment.Id,
            Provider = Name,
            RedirectUrl = session.Url,
            Completed = false,
        };
    }

    public async Task<bool> RefundAsync(Payment payment, int amountMinor, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(payment.ProviderRef)) return false;

        try
        {
            var client = new StripeClient(_payments.StripeSecretKey);

            // ProviderRef holds the Checkout Session id; the refund needs the
            // PaymentIntent behind it.
            var session = await new SessionService(client)
                .GetAsync(payment.ProviderRef, cancellationToken: ct);

            if (string.IsNullOrWhiteSpace(session.PaymentIntentId))
            {
                logger.LogWarning("Stripe session {Session} has no payment intent to refund", payment.ProviderRef);
                return false;
            }

            await new RefundService(client).CreateAsync(new RefundCreateOptions
            {
                PaymentIntent = session.PaymentIntentId,
                Amount = amountMinor,
            }, cancellationToken: ct);

            return true;
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Stripe refused a refund for payment {PaymentId}", payment.Id);
            return false;
        }
    }
}
