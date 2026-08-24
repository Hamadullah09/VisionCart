using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Common;
using VisionCart.Domain.Constants;

namespace VisionCart.Application.Admin;

public sealed class DashboardStats
{
    public int OrdersToday { get; init; }
    public int PaidLast30DaysMinor { get; init; }
    public int AwaitingPayment { get; init; }
    public int InTheLab { get; init; }
    public int PatientFiles { get; init; }
    public int LiveFrames { get; init; }
    public int PrescriptionsToCheck { get; init; }
    public int LowStockLines { get; init; }
}

public sealed class PendingPrescription
{
    public string Id { get; init; } = string.Empty;
    public string PatientId { get; init; } = string.Empty;
    public string PatientName { get; init; } = string.Empty;
    public string FileNo { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public DateTime IssuedAt { get; init; }
}

public sealed class RecentOrderRow
{
    public string Id { get; init; } = string.Empty;
    public string OrderNo { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public int TotalMinor { get; init; }
    public string FirstLine { get; init; } = string.Empty;
    public DateTime PlacedAt { get; init; }
}

public sealed class LowStockRow
{
    public string VariantId { get; init; } = string.Empty;
    public string FrameId { get; init; } = string.Empty;
    public string FrameName { get; init; } = string.Empty;
    public string ColorName { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public int StockQty { get; init; }
    public int LowStockAt { get; init; }
}

public sealed class DashboardView
{
    public DashboardStats Stats { get; init; } = new();
    public IReadOnlyList<PendingPrescription> PrescriptionQueue { get; init; } = [];
    public IReadOnlyList<RecentOrderRow> RecentOrders { get; init; } = [];
    public IReadOnlyList<LowStockRow> LowStock { get; init; } = [];
}

public interface IDashboardService
{
    Task<DashboardView> BuildAsync(CancellationToken ct = default);
}

/// <summary>
/// The back-office landing page: today's orders, money taken, prescriptions
/// waiting and low stock.
///
/// Every figure is a database aggregate rather than a count over loaded rows —
/// the legacy dashboard did the same, and it is what keeps the page fast once
/// the shop has real order volume.
/// </summary>
public sealed class DashboardService(IApplicationDbContext db, TimeProvider clock) : IDashboardService
{
    private const int QueueSize = 8;

    public async Task<DashboardView> BuildAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var startOfToday = now.Date;
        var thirtyDaysAgo = now.AddDays(-30);

        var stats = new DashboardStats
        {
            OrdersToday = await db.Orders.CountAsync(o => o.PlacedAt >= startOfToday, ct),

            PaidLast30DaysMinor = await db.Orders
                .Where(o => o.PaidAt != null && o.PaidAt >= thirtyDaysAgo)
                .SumAsync(o => (int?)o.TotalMinor, ct) ?? 0,

            AwaitingPayment = await db.Orders
                .CountAsync(o => o.PaymentStatus == PaymentStatuses.Unpaid
                                 && o.Status != OrderStatuses.Cancelled, ct),

            InTheLab = await db.Orders.CountAsync(o => o.Status == OrderStatuses.InLab, ct),

            PatientFiles = await db.Patients.CountAsync(p => p.DeletedAt == null, ct),

            LiveFrames = await db.Frames.CountAsync(f => f.Status == ProductStatuses.Active, ct),

            PrescriptionsToCheck = await db.Prescriptions
                .CountAsync(p => p.Status == RxStatuses.PendingVerification, ct),

            LowStockLines = await db.FrameVariants
                .CountAsync(v => v.IsActive && v.StockQty <= v.LowStockAt, ct),
        };

        var queue = await db.Prescriptions.AsNoTracking()
            .Where(p => p.Status == RxStatuses.PendingVerification)
            .OrderBy(p => p.IssuedAt)
            .Take(QueueSize)
            .Select(p => new PendingPrescription
            {
                Id = p.Id,
                PatientId = p.PatientId,
                PatientName = p.Patient.FirstName + " " + p.Patient.LastName,
                FileNo = p.Patient.FileNo,
                IssuedAt = p.IssuedAt,
                // Built here rather than in the view so the page needs no extra query.
                Summary = "OD " + (p.OdSphere == null ? "—" : p.OdSphere.ToString())
                          + " | OS " + (p.OsSphere == null ? "—" : p.OsSphere.ToString()),
            })
            .ToListAsync(ct);

        var recent = await db.Orders.AsNoTracking()
            .OrderByDescending(o => o.PlacedAt)
            .Take(QueueSize)
            .Select(o => new RecentOrderRow
            {
                Id = o.Id,
                OrderNo = o.OrderNo,
                Status = o.Status,
                PaymentStatus = o.PaymentStatus,
                TotalMinor = o.TotalMinor,
                PlacedAt = o.PlacedAt,
                FirstLine = o.Items.Select(i => i.TitleSnapshot).FirstOrDefault() ?? "",
            })
            .ToListAsync(ct);

        var lowStock = await db.FrameVariants.AsNoTracking()
            .Where(v => v.IsActive && v.StockQty <= v.LowStockAt)
            .OrderBy(v => v.StockQty)
            .Take(QueueSize)
            .Select(v => new LowStockRow
            {
                VariantId = v.Id,
                FrameId = v.FrameId,
                FrameName = v.Frame.Name,
                ColorName = v.ColorName,
                Sku = v.Sku,
                StockQty = v.StockQty,
                LowStockAt = v.LowStockAt,
            })
            .ToListAsync(ct);

        return new DashboardView
        {
            Stats = stats,
            PrescriptionQueue = queue,
            RecentOrders = recent,
            LowStock = lowStock,
        };
    }
}
