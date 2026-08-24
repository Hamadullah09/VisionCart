using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisionCart.Application.Common;
using VisionCart.Application.Platform;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;

namespace VisionCart.Application.Payments;

public sealed class PaymentMethodMeta
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    /// <summary>Customer completes payment in the browser before the order is confirmed.</summary>
    public bool Online { get; init; }
}

public sealed class StartPaymentResult
{
    public string PaymentId { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    /// <summary>Providers that take over the browser.</summary>
    public string? RedirectUrl { get; init; }
    /// <summary>Offline methods: what the customer should do next.</summary>
    public string? Instructions { get; init; }
    /// <summary>True when the order can be treated as placed without further action.</summary>
    public bool Completed { get; init; }
}

public sealed class PaymentOptions
{
    public const string SectionName = "Payments";
    /// <summary>Comma-separated, in the order they appear at checkout.</summary>
    public string Providers { get; set; } = "cod,bank_transfer";
    public string? StripeSecretKey { get; set; }
    public string? StripePublishableKey { get; set; }
    public string? StripeWebhookSecret { get; set; }
    public string BankTransferInstructions { get; set; } =
        "Transfer the order total to our account and email the receipt quoting your order number.";
}

/// <summary>
/// A payment integration. Offline methods are implemented here in Application;
/// Stripe lives in Infrastructure because it makes outbound calls.
/// </summary>
public interface IPaymentProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<StartPaymentResult> StartAsync(Order order, CancellationToken ct = default);
    Task<bool> RefundAsync(Payment payment, int amountMinor, CancellationToken ct = default);
}

public interface IPaymentService
{
    IReadOnlyList<PaymentMethodMeta> EnabledMethods();
    Task<StartPaymentResult> StartAsync(Order order, string method, CancellationToken ct = default);

    /// <summary>
    /// Mark an order paid. Called by the payment webhook and by staff confirming
    /// a bank transfer, so the state transition lives in exactly one place.
    /// </summary>
    Task<bool> MarkOrderPaidAsync(string orderId, string provider, string? providerRef,
        string? rawPayload, string? idempotencyKey, int? amountMinor = null, CancellationToken ct = default);

    Task MarkPaymentFailedAsync(string orderId, string provider, string error, CancellationToken ct = default);
    Task<ActionResult> RefundAsync(string paymentId, int? amountMinor, CancellationToken ct = default);
}

/// <summary>
/// Port of <c>src/lib/payments.ts</c>.
///
/// Which methods appear at checkout is driven by configuration, so adding a card
/// processor later is a config change plus one adapter. The browser is never
/// trusted to confirm payment: an online method is settled by its webhook.
/// </summary>
public sealed class PaymentService(
    IApplicationDbContext db,
    IEnumerable<IPaymentProvider> providers,
    IAuditService audit,
    IOptions<PaymentOptions> options,
    TimeProvider clock,
    ILogger<PaymentService> logger) : IPaymentService
{
    private readonly PaymentOptions _options = options.Value;

    private static readonly IReadOnlyDictionary<string, PaymentMethodMeta> AllMethods =
        new Dictionary<string, PaymentMethodMeta>
        {
            [PaymentProviders.Cod] = new()
            {
                Id = PaymentProviders.Cod,
                Label = "Cash on delivery",
                Description = "Pay the courier when your glasses arrive.",
                Online = false,
            },
            [PaymentProviders.BankTransfer] = new()
            {
                Id = PaymentProviders.BankTransfer,
                Label = "Bank transfer",
                Description = "Transfer to our account and send us the receipt.",
                Online = false,
            },
            [PaymentProviders.Stripe] = new()
            {
                Id = PaymentProviders.Stripe,
                Label = "Card payment",
                Description = "Visa, Mastercard and wallets. Secured by Stripe.",
                Online = true,
            },
        };

    public IReadOnlyList<PaymentMethodMeta> EnabledMethods()
    {
        var configured = _options.Providers
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return
        [
            .. configured
                .Select(id => AllMethods.GetValueOrDefault(id))
                .Where(m => m is not null)
                .Select(m => m!)
                // A provider without credentials would render a dead form — hide
                // it instead, exactly as the legacy implementation did.
                .Where(m => providers.FirstOrDefault(p =>
                    string.Equals(p.Name, m.Id, StringComparison.OrdinalIgnoreCase))
                    is not { IsConfigured: false }),
        ];
    }

    public async Task<StartPaymentResult> StartAsync(Order order, string method, CancellationToken ct = default)
    {
        if (!EnabledMethods().Any(m => m.Id == method))
            throw new InvalidOperationException($"Payment method \"{method}\" is not enabled for this store.");

        var provider = providers.FirstOrDefault(p =>
            string.Equals(p.Name, method, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No adapter registered for \"{method}\".");

        return await provider.StartAsync(order, ct);
    }

    /// <returns>False when the event was a duplicate and has already been applied.</returns>
    public async Task<bool> MarkOrderPaidAsync(
        string orderId, string provider, string? providerRef, string? rawPayload,
        string? idempotencyKey, int? amountMinor = null, CancellationToken ct = default)
    {
        // Replay protection. The legacy webhook had none: a provider redelivering
        // an event would mark the order paid a second time and double-count
        // revenue. The unique index on IdempotencyKey makes the database the
        // arbiter, so two concurrent deliveries cannot both win.
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var seen = await db.Payments
                .AsNoTracking()
                .AnyAsync(p => p.IdempotencyKey == idempotencyKey, ct);

            if (seen)
            {
                logger.LogInformation(
                    "Ignoring duplicate payment event {Key} for order {OrderId}", idempotencyKey, orderId);
                return false;
            }
        }

        var order = await db.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null)
        {
            logger.LogWarning("Payment event for unknown order {OrderId}", orderId);
            return false;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var amount = amountMinor ?? order.TotalMinor;
        var orderNo = order.OrderNo;

        try
        {
            await db.ExecuteInTransactionAsync(async token =>
            {
                // Re-read: the change tracker is cleared before each attempt.
                var tracked = await db.Orders
                    .Include(o => o.Payments)
                    .FirstAsync(o => o.Id == orderId, token);

                // Reuse the open payment row for this provider rather than
                // leaving a trail of duplicates behind a customer who reloads
                // the return page.
                var payment = tracked.Payments
                    .FirstOrDefault(p => p.Provider == provider && p.Status == "pending");

                if (payment is null)
                {
                    payment = new Payment
                    {
                        OrderId = tracked.Id,
                        Provider = provider,
                        AmountMinor = amount,
                        Currency = tracked.Currency,
                    };
                    db.Payments.Add(payment);
                }

                payment.Status = "succeeded";
                payment.AmountMinor = amount;
                payment.ProviderRef = providerRef ?? payment.ProviderRef;
                payment.RawPayload = rawPayload ?? payment.RawPayload;
                payment.IdempotencyKey = idempotencyKey ?? payment.IdempotencyKey;

                tracked.PaymentStatus = PaymentStatuses.Paid;
                tracked.PaidAt ??= now;
                // Only advance the order itself if it is still waiting; a shipped
                // order whose bank transfer lands late must not be dragged back.
                if (tracked.Status == OrderStatuses.Pending) tracked.Status = OrderStatuses.Paid;

                await db.SaveChangesAsync(token);
            }, ct);
        }
        catch (DbUpdateException ex)
        {
            // The unique index fired: a concurrent delivery of the same event won.
            logger.LogInformation(ex,
                "Concurrent duplicate payment event {Key} rejected by the database", idempotencyKey);
            return false;
        }

        await audit.WriteAsync(AuditActions.PaymentMarkPaid, "Order", orderId,
            new { orderNo, provider, amountMinor = amount }, ct);

        return true;
    }

    public async Task MarkPaymentFailedAsync(
        string orderId, string provider, string error, CancellationToken ct = default)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return;

        order.PaymentStatus = PaymentStatuses.Failed;
        order.InternalNotes = $"Payment could not be started: {error}";

        db.Payments.Add(new Payment
        {
            OrderId = order.Id,
            Provider = provider,
            Status = "failed",
            AmountMinor = order.TotalMinor,
            Currency = order.Currency,
            Error = error.Length > 512 ? error[..512] : error,
        });

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditActions.PaymentFailed, "Order", order.Id,
            new { orderNo = order.OrderNo, provider }, ct);
    }

    public async Task<ActionResult> RefundAsync(
        string paymentId, int? amountMinor, CancellationToken ct = default)
    {
        var payment = await db.Payments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);

        if (payment is null) return ActionResult.Fail("That payment no longer exists.");
        if (payment.Status != "succeeded") return ActionResult.Fail("Only a settled payment can be refunded.");

        var refundable = payment.AmountMinor - payment.RefundedMinor;
        var amount = amountMinor ?? refundable;

        if (amount <= 0) return ActionResult.Fail("There is nothing left to refund on this payment.");
        if (amount > refundable)
            return ActionResult.Fail($"At most {refundable} minor units remain refundable on this payment.");

        var provider = providers.FirstOrDefault(p =>
            string.Equals(p.Name, payment.Provider, StringComparison.OrdinalIgnoreCase));

        if (provider is { IsConfigured: true })
        {
            var ok = await provider.RefundAsync(payment, amount, ct);
            if (!ok) return ActionResult.Fail("The payment provider refused the refund.");
        }

        payment.RefundedMinor += amount;
        payment.Status = payment.RefundedMinor >= payment.AmountMinor ? "refunded" : "succeeded";

        var order = payment.Order;
        order.PaymentStatus = payment.RefundedMinor >= order.TotalMinor
            ? PaymentStatuses.Refunded
            : PaymentStatuses.PartiallyRefunded;

        if (order.PaymentStatus == PaymentStatuses.Refunded) order.Status = OrderStatuses.Refunded;

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditActions.PaymentRefund, "Order", order.Id,
            new { orderNo = order.OrderNo, amountMinor = amount, provider = payment.Provider }, ct);

        return ActionResult.Success();
    }
}

/// <summary>Cash on delivery. Works immediately, no account needed.</summary>
public sealed class CashOnDeliveryProvider(IApplicationDbContext db) : IPaymentProvider
{
    public string Name => PaymentProviders.Cod;
    public bool IsConfigured => true;

    public async Task<StartPaymentResult> StartAsync(Order order, CancellationToken ct = default)
    {
        var payment = await OfflinePayment.OpenAsync(db, order, Name, ct);
        return new StartPaymentResult
        {
            PaymentId = payment.Id,
            Provider = Name,
            Completed = true,
            Instructions = "Have the exact amount ready for the courier. We'll call before delivery.",
        };
    }

    public Task<bool> RefundAsync(Payment payment, int amountMinor, CancellationToken ct = default) =>
        // Cash refunds are handled at the counter; nothing to call.
        Task.FromResult(true);
}

/// <summary>Customer transfers manually; staff confirm from the order page.</summary>
public sealed class BankTransferProvider(IApplicationDbContext db, IOptions<PaymentOptions> options)
    : IPaymentProvider
{
    public string Name => PaymentProviders.BankTransfer;
    public bool IsConfigured => true;

    public async Task<StartPaymentResult> StartAsync(Order order, CancellationToken ct = default)
    {
        var payment = await OfflinePayment.OpenAsync(db, order, Name, ct);
        return new StartPaymentResult
        {
            PaymentId = payment.Id,
            Provider = Name,
            Completed = true,
            Instructions = options.Value.BankTransferInstructions,
        };
    }

    public Task<bool> RefundAsync(Payment payment, int amountMinor, CancellationToken ct = default) =>
        Task.FromResult(true);
}

internal static class OfflinePayment
{
    /// <summary>
    /// Reuse the open payment row for offline methods so a customer who returns
    /// to the confirmation page doesn't leave a trail of duplicate pendings.
    /// </summary>
    public static async Task<Payment> OpenAsync(
        IApplicationDbContext db, Order order, string provider, CancellationToken ct)
    {
        var existing = await db.Payments
            .Where(p => p.OrderId == order.Id && p.Provider == provider && p.Status == "pending")
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            existing.AmountMinor = order.TotalMinor;
            existing.Currency = order.Currency;
            await db.SaveChangesAsync(ct);
            return existing;
        }

        var payment = new Payment
        {
            OrderId = order.Id,
            Provider = provider,
            Status = "pending",
            AmountMinor = order.TotalMinor,
            Currency = order.Currency,
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);
        return payment;
    }
}
