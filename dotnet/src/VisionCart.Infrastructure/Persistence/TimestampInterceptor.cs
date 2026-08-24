using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace VisionCart.Infrastructure.Persistence;

/// <summary>
/// Reproduces Prisma's <c>@updatedAt</c>. Prisma stamped the column on every
/// write automatically; EF Core has no equivalent, so without this the
/// UpdatedAt columns carried over from the legacy schema would silently freeze
/// at their creation value.
///
/// Applied by convention: any entity with a writable <c>UpdatedAt</c> of type
/// <see cref="DateTime"/> is stamped on insert and update. UTC throughout —
/// the legacy system stored UTC and the shop serves more than one timezone.
/// </summary>
public sealed class TimestampInterceptor : SaveChangesInterceptor
{
    private const string UpdatedAt = nameof(UpdatedAt);
    private const string CreatedAt = nameof(CreatedAt);

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Stamp(DbContext? context)
    {
        if (context is null) return;
        var now = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;

            if (entry.State == EntityState.Added
                && entry.Metadata.FindProperty(CreatedAt) is { ClrType: var ct } && ct == typeof(DateTime)
                && entry.Property(CreatedAt).CurrentValue is DateTime created && created == default)
            {
                entry.Property(CreatedAt).CurrentValue = now;
            }

            if (entry.Metadata.FindProperty(UpdatedAt) is { ClrType: var ut } && ut == typeof(DateTime))
            {
                entry.Property(UpdatedAt).CurrentValue = now;
            }
        }
    }
}
