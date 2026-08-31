using Microsoft.EntityFrameworkCore;
using VisionCart.Domain.Entities;
using VisionCart.Infrastructure.Persistence;

namespace VisionCart.IntegrationTests;

/// <summary>
/// Verifies that the SQL Server schema produced by the EF Core migration actually
/// carries over what the Prisma schema guaranteed.
///
/// These assertions run against a real SQL Server instance, not an in-memory
/// provider, because every finding worth catching here — cascade-path rejection,
/// nvarchar(max) columns that cannot be indexed, filtered unique indexes — only
/// exists in the real engine.
/// </summary>
public class SchemaMigrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\VisionCartDev;Database=VisionCart;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    private ApplicationDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        _db = new ApplicationDbContext(options);
        await _db.Database.MigrateAsync();
    }

    public Task DisposeAsync() { _db.Dispose(); return Task.CompletedTask; }

    private async Task<int> ScalarAsync(string sql)
    {
        await using var command = _db.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<List<string>> StringsAsync(string sql)
    {
        var results = new List<string>();
        await using var command = _db.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) results.Add(reader.GetString(0));
        return results;
    }

    [Fact]
    public async Task All_27_legacy_tables_plus_migration_additions_exist()
    {
        var tables = await StringsAsync(
            "SELECT name FROM sys.tables WHERE is_ms_shipped = 0 ORDER BY name");

        // The 27 tables carried over from the Prisma schema. User maps to
        // AspNetUsers, which Identity owns, so it is asserted separately below.
        string[] legacy =
        [
            "Address", "Appointment", "AuditLog", "Brand", "Cart", "CartItem",
            "Category", "Frame", "FrameCategory", "FrameVariant", "ImportJob",
            "LensOption", "MediaAsset", "Order", "OrderItem", "Patient",
            "PatientDocument", "Payment", "Prescription", "ProductImage",
            "Promotion", "Setting", "Shipment", "ShippingRate", "TryOnSession",
            "TryOnSnapshot",
        ];

        foreach (var table in legacy)
            Assert.Contains(table, tables);

        Assert.Contains("AspNetUsers", tables);

        // Added during the migration to close production gaps.
        Assert.Contains("OutboxEmail", tables);
        Assert.Contains("DataSubjectRequest", tables);
    }

    [Fact]
    public async Task Money_columns_are_all_integers()
    {
        // The single most important invariant carried over from the legacy system:
        // money is an integer count of minor units, never a float or decimal.
        var nonInteger = await StringsAsync("""
            SELECT t.name + '.' + c.name
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE t.is_ms_shipped = 0
              AND c.name LIKE '%Minor'
              AND ty.name NOT IN ('int', 'bigint')
            """);

        Assert.Empty(nonInteger);

        // And there is a meaningful number of them, so the query is not vacuous.
        var moneyColumns = await ScalarAsync("""
            SELECT COUNT(*) FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            WHERE t.is_ms_shipped = 0 AND c.name LIKE '%Minor'
            """);
        // 25 money columns across 10 tables. Asserted exactly so that adding or
        // dropping one is a deliberate, visible decision rather than a silent
        // drift. The twenty-fifth is Frame.LastCostMinor - what the last
        // delivery of a frame cost, added with the vendor record.
        Assert.Equal(25, moneyColumns);
    }

    [Fact]
    public async Task Unique_constraints_from_the_prisma_schema_survive()
    {
        // Each of these was @unique in Prisma and is load-bearing: order numbers
        // and patient file numbers are quoted to customers, and SKUs and slugs
        // are the keys CSV import matches rows on.
        (string Table, string Column)[] expected =
        [
            ("Order", "OrderNo"),
            ("Patient", "FileNo"),
            ("Frame", "Sku"),
            ("Frame", "Slug"),
            ("FrameVariant", "Sku"),
            ("Brand", "Name"),
            ("Brand", "Slug"),
            ("Category", "Slug"),
            ("LensOption", "Code"),
            ("Cart", "Token"),
        ];

        foreach (var (table, column) in expected)
        {
            var count = await ScalarAsync($"""
                SELECT COUNT(*)
                FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE i.is_unique = 1
                  AND OBJECT_NAME(i.object_id) = '{table}'
                  AND c.name = '{column}'
                """);
            Assert.True(count > 0, $"expected a unique index on {table}.{column}");
        }
    }

    [Fact]
    public async Task Nullable_promo_code_allows_many_automatic_promotions()
    {
        // Prisma's @unique on a nullable column permitted unlimited NULLs. A plain
        // SQL Server unique index permits exactly one, which would have silently
        // capped the shop at a single automatic (code-free) promotion. The
        // configuration reproduces the original behaviour with a filtered index;
        // this proves the filter is actually present.
        var filtered = await ScalarAsync("""
            SELECT COUNT(*) FROM sys.indexes
            WHERE OBJECT_NAME(object_id) = 'Promotion'
              AND is_unique = 1 AND has_filter = 1
            """);
        Assert.True(filtered > 0, "Promotion.Code needs a FILTERED unique index");

        // Behavioural proof: two code-free promotions can coexist.
        _db.Promotions.Add(new Promotion { Name = "Auto A", Kind = "free_shipping", Code = null });
        _db.Promotions.Add(new Promotion { Name = "Auto B", Kind = "free_shipping", Code = null });
        await _db.SaveChangesAsync();

        var autos = await _db.Promotions.CountAsync(p => p.Code == null);
        Assert.True(autos >= 2);

        _db.Promotions.RemoveRange(_db.Promotions.Where(p => p.Name == "Auto A" || p.Name == "Auto B"));
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Prescription_used_by_an_order_cannot_be_deleted()
    {
        // The strongest guarantee gained in the migration. Prisma set the order
        // line's prescriptionId to NULL when a prescription was deleted, so the
        // link to clinical history could be severed. SQL Server's cascade rules
        // forced this relationship to Restrict, which turns "prescriptions are
        // immutable once used" from a convention into a database constraint.
        //
        // Asserted with raw SQL rather than through the change tracker: what
        // protects production is the constraint in the database, and a DELETE can
        // reach the table from a maintenance script or a support session that EF
        // never sees.
        var patient = new Patient
        {
            FileNo = "P-TEST-" + Guid.NewGuid().ToString("N")[..8],
            FirstName = "Schema",
            LastName = "Test",
        };
        var rx = new Prescription { Patient = patient, OdSphere = -2.25, Status = "verified" };
        var order = new Order { OrderNo = "TEST-" + Guid.NewGuid().ToString("N")[..8], Email = "t@example.com" };
        var line = new OrderItem
        {
            Order = order,
            Prescription = rx,
            TitleSnapshot = "Test frame",
            SkuSnapshot = "TEST-SKU",
        };

        _db.AddRange(patient, rx, order, line);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            _db.Database.ExecuteSqlRawAsync("DELETE FROM [Prescription] WHERE [Id] = {0}", rx.Id));
        Assert.Contains("REFERENCE constraint", ex.Message, StringComparison.OrdinalIgnoreCase);

        // The prescription is still there, and still linked to the order line.
        _db.ChangeTracker.Clear();
        var stillLinked = await _db.OrderItems
            .AsNoTracking()
            .FirstAsync(i => i.Id == line.Id);
        Assert.Equal(rx.Id, stillLinked.PrescriptionId);

        // Clean up in dependency order.
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM [OrderItem] WHERE [Id] = {0}", line.Id);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM [Order] WHERE [Id] = {0}", order.Id);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM [Prescription] WHERE [Id] = {0}", rx.Id);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM [Patient] WHERE [Id] = {0}", patient.Id);
    }

    [Fact]
    public void Order_line_relationships_disable_client_side_fixup()
    {
        // DeleteBehavior.NoAction would emit the same restrictive foreign key, but
        // leaves EF Core free to null the FK in memory and UPDATE it away before
        // the DELETE — severing the link by the back door. Restrict disables that.
        var orderItem = _db.Model.FindEntityType(typeof(OrderItem))!;

        foreach (var navigation in new[] { nameof(OrderItem.Prescription), nameof(OrderItem.Variant) })
        {
            var fk = orderItem.GetForeignKeys()
                .Single(f => f.DependentToPrincipal?.Name == navigation);
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        }
    }

    [Fact]
    public async Task No_indexed_column_is_nvarchar_max()
    {
        // Prisma emitted unbounded TEXT for every string. SQL Server cannot index
        // nvarchar(max); had any indexed column slipped through, the migration
        // would have applied but the index would be missing or the insert would
        // fail at runtime.
        var offenders = await StringsAsync("""
            SELECT DISTINCT OBJECT_NAME(i.object_id) + '.' + c.name
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            JOIN sys.tables t ON t.object_id = i.object_id
            WHERE t.is_ms_shipped = 0 AND ty.name = 'nvarchar' AND c.max_length = -1
            """);

        Assert.Empty(offenders);
    }

    [Fact]
    public async Task Payment_webhook_replay_is_blocked_by_the_database()
    {
        // Added during the migration. The legacy webhook had no replay protection.
        var order = new Order { OrderNo = "IDEM-" + Guid.NewGuid().ToString("N")[..8], Email = "t@example.com" };
        var key = "evt_" + Guid.NewGuid().ToString("N");
        _db.Add(order);
        _db.Add(new Payment { Order = order, Provider = "stripe", AmountMinor = 1000, IdempotencyKey = key });
        await _db.SaveChangesAsync();

        _db.Add(new Payment { Order = order, Provider = "stripe", AmountMinor = 1000, IdempotencyKey = key });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => _db.SaveChangesAsync());

        _db.ChangeTracker.Clear();
        _db.Payments.RemoveRange(_db.Payments.Where(p => p.OrderId == order.Id));
        await _db.SaveChangesAsync();
        _db.Orders.Remove(await _db.Orders.FirstAsync(o => o.Id == order.Id));
        await _db.SaveChangesAsync();
    }
}
