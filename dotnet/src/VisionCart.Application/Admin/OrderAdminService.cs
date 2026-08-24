using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Common;
using VisionCart.Application.Email;
using VisionCart.Application.Payments;
using VisionCart.Application.Platform;
using VisionCart.Application.Shipping;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;

namespace VisionCart.Application.Admin;

public sealed class OrderFilters
{
    public string? Q { get; init; }
    public string? Status { get; init; }
    public string? PaymentStatus { get; init; }
    public string? LabStatus { get; init; }
    public int Page { get; init; } = 1;
    public int PerPage { get; init; } = 25;
}

public sealed class OrderRow
{
    public string Id { get; init; } = string.Empty;
    public string OrderNo { get; init; } = string.Empty;
    public DateTime PlacedAt { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? PatientName { get; init; }
    public string? FileNo { get; init; }
    public string Items { get; init; } = string.Empty;
    public int TotalMinor { get; init; }
    public string Status { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
}

public interface IOrderAdminService
{
    Task<PagedResult<OrderRow>> ListAsync(OrderFilters filters, CancellationToken ct = default);
    Task<int> PaidTotalAsync(OrderFilters filters, CancellationToken ct = default);
    Task<Order?> GetAsync(string id, CancellationToken ct = default);

    Task<ActionResult> UpdateStatusAsync(string orderId, string? status, string? fulfilmentStatus,
        string? internalNotes, CancellationToken ct = default);

    Task<ActionResult> UpdateLabStatusAsync(string orderItemId, string labStatus, string? labRef,
        CancellationToken ct = default);

    Task<ActionResult> RecordManualPaymentAsync(string orderId, string? reference,
        CancellationToken ct = default);

    Task<ActionResult> RefundAsync(string paymentId, decimal? amountMajor, CancellationToken ct = default);

    Task<ActionResult> CreateShipmentAsync(string orderId, string carrier, string? trackingNumber,
        string? trackingUrl, string? rateRef, CancellationToken ct = default);
}

/// <summary>
/// The order screens: the lab ticket, payment confirmation, refunds and
/// dispatch. Port of the order half of <c>src/app/actions/admin.ts</c>.
///
/// Every mutation here is audited, and every one that changes what the customer
/// should know about queues an email — the notification gap the legacy system
/// left open.
/// </summary>
public sealed class OrderAdminService(
    IApplicationDbContext db,
    IPaymentService payments,
    IShippingService shipping,
    IEmailService email,
    IAuditService audit,
    TimeProvider clock) : IOrderAdminService
{
    public async Task<PagedResult<OrderRow>> ListAsync(
        OrderFilters filters, CancellationToken ct = default)
    {
        var query = Filtered(filters);

        var total = await query.CountAsync(ct);
        var perPage = Math.Clamp(filters.PerPage, 1, 100);
        var page = Math.Max(1, filters.Page);

        var rows = await query
            .OrderByDescending(o => o.PlacedAt)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(o => new OrderRow
            {
                Id = o.Id,
                OrderNo = o.OrderNo,
                PlacedAt = o.PlacedAt,
                Email = o.Email,
                PatientName = o.Patient == null ? null : o.Patient.FirstName + " " + o.Patient.LastName,
                FileNo = o.Patient == null ? null : o.Patient.FileNo,
                TotalMinor = o.TotalMinor,
                Status = o.Status,
                PaymentStatus = o.PaymentStatus,
                Items = string.Join(", ", o.Items.Select(i => i.TitleSnapshot)),
            })
            .ToListAsync(ct);

        return new PagedResult<OrderRow> { Items = rows, Total = total, Page = page, PerPage = perPage };
    }

    /// <summary>Money actually taken across the filtered set, for the list header.</summary>
    public async Task<int> PaidTotalAsync(OrderFilters filters, CancellationToken ct = default) =>
        await Filtered(filters)
            .Where(o => o.PaymentStatus == PaymentStatuses.Paid)
            .SumAsync(o => (int?)o.TotalMinor, ct) ?? 0;

    private IQueryable<Order> Filtered(OrderFilters filters)
    {
        var query = db.Orders.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filters.Q))
        {
            var term = filters.Q.Trim();
            query = query.Where(o =>
                EF.Functions.Like(o.OrderNo, $"%{term}%")
                || EF.Functions.Like(o.Email, $"%{term}%")
                || (o.Phone != null && EF.Functions.Like(o.Phone, $"%{term}%")));
        }

        if (!string.IsNullOrEmpty(filters.Status)) query = query.Where(o => o.Status == filters.Status);
        if (!string.IsNullOrEmpty(filters.PaymentStatus))
            query = query.Where(o => o.PaymentStatus == filters.PaymentStatus);
        if (!string.IsNullOrEmpty(filters.LabStatus))
            query = query.Where(o => o.Items.Any(i => i.LabStatus == filters.LabStatus));

        return query;
    }

    public async Task<Order?> GetAsync(string id, CancellationToken ct = default) =>
        await db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Prescription)
            .Include(o => o.Payments)
            .Include(o => o.Shipments)
            .Include(o => o.ShippingAddress)
            .Include(o => o.Patient)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<ActionResult> UpdateStatusAsync(
        string orderId, string? status, string? fulfilmentStatus, string? internalNotes,
        CancellationToken ct = default)
    {
        var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return ActionResult.Fail("That order no longer exists.");

        if (!string.IsNullOrEmpty(status) && !OrderStatuses.All.Contains(status))
            return ActionResult.Fail("That is not a valid order status.");

        if (!string.IsNullOrEmpty(fulfilmentStatus) && !FulfilmentStatuses.All.Contains(fulfilmentStatus))
            return ActionResult.Fail("That is not a valid fulfilment status.");

        var previousStatus = order.Status;
        var now = clock.GetUtcNow().UtcDateTime;

        if (!string.IsNullOrEmpty(status) && status != order.Status)
        {
            // Cancelling returns the frames to stock. Doing it here rather than
            // leaving it to staff is the difference between an accurate stock
            // figure and a slow drift into overselling.
            if (status == OrderStatuses.Cancelled && order.Status != OrderStatuses.Cancelled)
            {
                foreach (var line in order.Items.Where(i => i.VariantId is not null))
                {
                    var variant = await db.FrameVariants.FirstOrDefaultAsync(v => v.Id == line.VariantId, ct);
                    if (variant is not null) variant.StockQty += line.Qty;
                }
                order.CancelledAt = now;
            }

            order.Status = status;
            if (status == OrderStatuses.Delivered) order.DeliveredAt ??= now;
        }

        if (!string.IsNullOrEmpty(fulfilmentStatus)) order.FulfilmentStatus = fulfilmentStatus;
        if (internalNotes is not null) order.InternalNotes = internalNotes;

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.OrderUpdate, "Order", order.Id, new
        {
            orderNo = order.OrderNo,
            from = previousStatus,
            to = order.Status,
            fulfilment = order.FulfilmentStatus,
        }, ct);

        // Tell the customer, but only about transitions that mean something to them.
        if (order.Status != previousStatus
            && order.Status is OrderStatuses.InLab or OrderStatuses.Ready
                or OrderStatuses.Delivered or OrderStatuses.Cancelled or OrderStatuses.Refunded)
        {
            await email.QueueOrderStatusAsync(order.Id, order.Status, ct);
        }

        return ActionResult.Success();
    }

    public async Task<ActionResult> UpdateLabStatusAsync(
        string orderItemId, string labStatus, string? labRef, CancellationToken ct = default)
    {
        if (!LabStatuses.All.Contains(labStatus))
            return ActionResult.Fail("That is not a valid lab stage.");

        var line = await db.OrderItems
            .Include(i => i.Order)
            .Include(i => i.Prescription)
            .FirstOrDefaultAsync(i => i.Id == orderItemId, ct);

        if (line is null) return ActionResult.Fail("That order line no longer exists.");

        // The clinical gate, carried over from the legacy lab ticket: a line
        // cannot be marked ready while its prescription is unverified.
        if (labStatus == LabStatuses.Ready
            && line.Prescription is not null
            && line.Prescription.Status != RxStatuses.Verified)
        {
            return ActionResult.Fail(
                "This prescription hasn't been verified by an optician yet. " +
                "Verify it on the patient file before marking the lenses ready.");
        }

        line.LabStatus = labStatus;
        if (labRef is not null) line.LabRef = string.IsNullOrWhiteSpace(labRef) ? null : labRef.Trim();

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.OrderUpdate, "OrderItem", line.Id, new
        {
            orderNo = line.Order.OrderNo,
            labStatus,
        }, ct);

        return ActionResult.Success();
    }

    public async Task<ActionResult> RecordManualPaymentAsync(
        string orderId, string? reference, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return ActionResult.Fail("That order no longer exists.");
        if (order.PaymentStatus == PaymentStatuses.Paid)
            return ActionResult.Fail("That order is already marked paid.");

        var provider = await db.Payments.AsNoTracking()
            .Where(p => p.OrderId == orderId)
            .Select(p => p.Provider)
            .FirstOrDefaultAsync(ct) ?? PaymentProviders.Manual;

        // Staff confirming a bank transfer go through exactly the same state
        // transition the webhook uses, so the two can never diverge.
        var applied = await payments.MarkOrderPaidAsync(
            orderId, provider, reference, rawPayload: null,
            idempotencyKey: $"manual:{orderId}", ct: ct);

        if (!applied) return ActionResult.Fail("That payment has already been recorded.");

        await email.QueuePaymentConfirmationAsync(orderId, ct);
        return ActionResult.Success();
    }

    public async Task<ActionResult> RefundAsync(
        string paymentId, decimal? amountMajor, CancellationToken ct = default)
    {
        var currency = await db.Payments.AsNoTracking()
            .Where(p => p.Id == paymentId).Select(p => p.Currency).FirstOrDefaultAsync(ct);

        if (currency is null) return ActionResult.Fail("That payment no longer exists.");

        var minor = amountMajor is { } major
            ? Domain.ValueObjects.Money.ToMinor(major, currency)
            : (int?)null;

        return await payments.RefundAsync(paymentId, minor, ct);
    }

    public async Task<ActionResult> CreateShipmentAsync(
        string orderId, string carrier, string? trackingNumber, string? trackingUrl,
        string? rateRef, CancellationToken ct = default)
    {
        if (!Carriers.All.Contains(carrier)) return ActionResult.Fail("Choose a carrier.");

        var order = await db.Orders.Include(o => o.Shipments)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null) return ActionResult.Fail("That order no longer exists.");

        // With a live carrier configured this buys a real label; otherwise it
        // records the courier details staff typed. Either way the order moves on.
        var label = await shipping.BuyLabelAsync(rateRef, ct);

        var shipment = order.Shipments.FirstOrDefault(s => s.Status == ShipmentStatuses.Pending)
                       ?? new Shipment { OrderId = order.Id };

        if (string.IsNullOrEmpty(shipment.Id) || !order.Shipments.Contains(shipment))
            db.Shipments.Add(shipment);

        shipment.Carrier = carrier;
        shipment.TrackingNumber = label?.TrackingNumber ?? trackingNumber;
        shipment.TrackingUrl = label?.TrackingUrl ?? trackingUrl;
        shipment.LabelUrl = label?.LabelUrl ?? shipment.LabelUrl;
        shipment.ProviderRef = label?.ProviderRef ?? shipment.ProviderRef;
        shipment.Status = "in_transit";
        shipment.ShippedAt = clock.GetUtcNow().UtcDateTime;

        order.Status = OrderStatuses.Shipped;
        order.FulfilmentStatus = "shipped";
        order.ShippedAt ??= clock.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.OrderUpdate, "Shipment", shipment.Id, new
        {
            orderNo = order.OrderNo,
            carrier,
            hasTracking = !string.IsNullOrWhiteSpace(shipment.TrackingNumber),
        }, ct);

        await email.QueueShipmentAsync(order.Id, shipment.Id, ct);
        return ActionResult.Success();
    }
}
