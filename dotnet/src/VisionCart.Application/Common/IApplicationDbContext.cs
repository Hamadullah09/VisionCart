using Microsoft.EntityFrameworkCore;
using VisionCart.Domain.Entities;

namespace VisionCart.Application.Common;

/// <summary>
/// The database as the application layer sees it.
///
/// Services compose LINQ against these sets; Infrastructure supplies the real
/// <c>ApplicationDbContext</c>. This keeps the service layer testable and stops
/// provider concerns leaking upwards, without inventing a repository per table —
/// EF Core's <see cref="DbSet{TEntity}"/> already is one.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<ApplicationUser> Users { get; }
    DbSet<Patient> Patients { get; }
    DbSet<Prescription> Prescriptions { get; }
    DbSet<PatientDocument> PatientDocuments { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<Address> Addresses { get; }

    DbSet<Brand> Brands { get; }
    DbSet<Vendor> Vendors { get; }
    DbSet<Category> Categories { get; }
    DbSet<FrameCategory> FrameCategories { get; }
    DbSet<Frame> Frames { get; }
    DbSet<FrameVariant> FrameVariants { get; }
    DbSet<ProductImage> ProductImages { get; }
    DbSet<LensOption> LensOptions { get; }

    DbSet<Cart> Carts { get; }
    DbSet<CartItem> CartItems { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Shipment> Shipments { get; }
    DbSet<ShippingRate> ShippingRates { get; }
    DbSet<Promotion> Promotions { get; }

    DbSet<TryOnSession> TryOnSessions { get; }
    DbSet<TryOnSnapshot> TryOnSnapshots { get; }

    DbSet<Setting> Settings { get; }
    DbSet<MediaAsset> MediaAssets { get; }
    DbSet<ImportJob> ImportJobs { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<DataSubjectRequest> DataSubjectRequests { get; }
    DbSet<OutboxEmail> OutboxEmails { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a multi-table unit of work inside a transaction, exactly as the
    /// legacy <c>prisma.$transaction</c> calls did.
    ///
    /// Callers must not open the transaction themselves. SQL Server connection
    /// resiliency is switched on for shared hosting, and EF Core refuses to mix a
    /// retrying execution strategy with a hand-rolled transaction — a retry would
    /// replay only part of it. Passing the whole unit here makes the transaction
    /// the retry boundary, so a transient failure re-runs all of it or none.
    ///
    /// The delegate may run more than once, so it must re-read anything it
    /// mutates rather than closing over previously tracked entities.
    /// </summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);
}
