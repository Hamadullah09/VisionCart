using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VisionCart.Application.Common;
using VisionCart.Domain.Entities;

namespace VisionCart.Infrastructure.Persistence;

/// <summary>
/// The VisionCart database. Replaces the Prisma client of the legacy system.
///
/// Identity's own seven tables are added by <see cref="IdentityDbContext{TUser,TRole,TKey}"/>
/// on top of the 27 tables carried over from the Prisma schema, plus four tables
/// added during the migration to close production gaps
/// (<see cref="OutboxEmail"/>, <see cref="DataSubjectRequest"/> and the audit/media
/// columns documented on those entities).
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options), IApplicationDbContext
{
    // --- People & access ---------------------------------------------------
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<PatientDocument> PatientDocuments => Set<PatientDocument>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Address> Addresses => Set<Address>();

    // --- Catalogue ---------------------------------------------------------
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<FrameCategory> FrameCategories => Set<FrameCategory>();
    public DbSet<Frame> Frames => Set<Frame>();
    public DbSet<FrameVariant> FrameVariants => Set<FrameVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<LensOption> LensOptions => Set<LensOption>();

    // --- Commerce ----------------------------------------------------------
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShippingRate> ShippingRates => Set<ShippingRate>();
    public DbSet<Promotion> Promotions => Set<Promotion>();

    // --- Try-on ------------------------------------------------------------
    public DbSet<TryOnSession> TryOnSessions => Set<TryOnSession>();
    public DbSet<TryOnSnapshot> TryOnSnapshots => Set<TryOnSnapshot>();

    // --- Platform ----------------------------------------------------------
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DataSubjectRequest> DataSubjectRequests => Set<DataSubjectRequest>();
    public DbSet<OutboxEmail> OutboxEmails => Set<OutboxEmail>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Identity's default table names are kept (AspNetUsers, AspNetRoles, ...)
        // so anyone familiar with an ASP.NET Core application finds them where
        // they expect. The 27 migrated tables keep their original singular names.

        foreach (var entity in builder.Model.GetEntityTypes())
        {
            // Prisma stored every string as unbounded TEXT. SQL Server cannot
            // index nvarchar(max), so any column without an explicit length set
            // in a configuration below gets a sane bounded default rather than
            // silently becoming unindexable.
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(string) && property.GetMaxLength() is null)
                {
                    property.SetMaxLength(DefaultStringLength);
                }

                // Money is an integer count of minor units everywhere. Guard the
                // invariant at the model level: no decimal/float column may ever
                // appear on a *Minor property.
                if (property.Name.EndsWith("Minor", StringComparison.Ordinal)
                    && property.ClrType != typeof(int) && property.ClrType != typeof(int?))
                {
                    throw new InvalidOperationException(
                        $"{entity.ClrType.Name}.{property.Name} stores money and must be an " +
                        "integer of minor units, not " + property.ClrType.Name + ".");
                }
            }
        }
    }

    /// <summary>
    /// Runs a unit of work inside a transaction that the retrying execution
    /// strategy treats as a single retriable operation.
    ///
    /// The change tracker is cleared before each attempt: a retry follows a
    /// failed SaveChanges, and entities left tracked from that attempt would
    /// otherwise be re-inserted, producing duplicates.
    /// </summary>
    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        var strategy = Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(async token =>
        {
            ChangeTracker.Clear();

            await using var transaction = await Database.BeginTransactionAsync(token);
            await operation(token);
            await transaction.CommitAsync(token);
        }, cancellationToken);
    }

    /// <summary>
    /// Bounded default for any string column not given an explicit length.
    /// Long free-text and JSON columns opt into nvarchar(max) explicitly.
    /// </summary>
    public const int DefaultStringLength = 512;
}
