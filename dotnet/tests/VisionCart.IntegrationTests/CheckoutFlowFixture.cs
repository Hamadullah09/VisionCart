using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VisionCart.Application.Admin;
using VisionCart.Application.Accounts;
using VisionCart.Application.Appointments;
using VisionCart.Application.Privacy;
using VisionCart.Application.Carts;
using VisionCart.Application.DataTransfer;
using VisionCart.Application.Media;
using VisionCart.Application.Storage;
using VisionCart.Application.Email;
using VisionCart.Application.Catalogue;
using VisionCart.Application.Checkout;
using VisionCart.Application.Common;
using VisionCart.Application.Patients;
using VisionCart.Application.Payments;
using VisionCart.Application.Platform;
using VisionCart.Application.Pricing;
using VisionCart.Application.Promotions;
using VisionCart.Application.Shipping;
using VisionCart.Infrastructure.Email;
using VisionCart.Infrastructure.Storage;
using VisionCart.Domain.Constants;
using VisionCart.Infrastructure.Persistence;

namespace VisionCart.IntegrationTests;

/// <summary>
/// Boots the real service graph against the real SQL Server database, without
/// the web host. Every service under test is the production implementation —
/// only the two HTTP-shaped abstractions (the cart cookie and the current user)
/// are supplied as test doubles, because there is no request in scope.
/// </summary>
public sealed class CheckoutFlowFixture : IDisposable
{
    public const string ConnectionString =
        @"Server=(localdb)\VisionCartDev;Database=VisionCart;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    private readonly ServiceProvider _provider;

    public CheckoutFlowFixture()
    {
        var services = new ServiceCollection();

        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(ConnectionString,
            sql => sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.Configure<StoreOptions>(o => { o.Currency = "PKR"; o.CurrencySymbol = "Rs."; });
        services.Configure<TaxOptions>(o => { o.RateBps = 0; o.Inclusive = false; });
        services.Configure<PaymentOptions>(o => o.Providers = "cod,bank_transfer");
        services.Configure<ShippingOptions>(o => o.Provider = "table_rate");
        services.Configure<EmailOptions>(o => o.Driver = "log");

        services.AddSingleton<ICartTokenAccessor, InMemoryCartToken>();
        services.AddSingleton<ICurrentUser, SystemUser>();

        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IPricingService, PricingService>();
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IPatientService, PatientService>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IShippingService, ShippingService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentProvider, CashOnDeliveryProvider>();
        services.AddScoped<IPaymentProvider, BankTransferProvider>();

        // Mail is queued to the outbox, never sent: the tests assert on the queue
        // rather than on a mock, which is what the production path actually does.
        services.AddScoped<IEmailService, EmailService>();
        services.AddSingleton<IEmailSender, LoggingEmailSender>();

        // Back office.
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IOrderAdminService, OrderAdminService>();
        services.AddScoped<IPatientAdminService, PatientAdminService>();
        services.AddScoped<ICatalogueAdminService, CatalogueAdminService>();
        services.AddScoped<IPlatformAdminService, PlatformAdminService>();

        // Media and data transfer use the real local storage provider, writing
        // into a scratch directory rather than a mock — the image pipeline is
        // half of what these tests are checking.
        services.Configure<StorageOptions>(o => o.LocalDirectory = "test-uploads");
        services.AddSingleton<IWebHostEnvironment>(_ => new TestWebHostEnvironment());
        services.AddScoped<IStorageProvider, LocalStorageProvider>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IExportService, ExportService>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IAddressService, AddressService>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<IDataSubjectService, DataSubjectService>();

        _provider = services.BuildServiceProvider();

        // The suite runs against a migrated, seeded database.
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();
    }

    public IServiceScope NewScope() => _provider.CreateScope();

    /// <summary>Fresh cart token per test, so tests cannot share a bag.</summary>
    public void ResetCart() => InMemoryCartToken.Reset();

    /// <summary>
    /// Returns a sellable colourway with at least <paramref name="minStock"/> units,
    /// topping the shelf up first.
    ///
    /// Every order a test places decrements real stock, and nothing puts it back. A
    /// test that merely *looked* for a well-stocked variant therefore passed on a
    /// fresh database and failed once the suite had been run enough times — the seed
    /// drains a little on each run. Stock a test depends on is part of its arrange
    /// step, so we arrange it.
    /// </summary>
    public async Task<string> SellableVariantIdAsync(int minStock = 2)
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var variant = await db.FrameVariants
            .Where(v => v.IsActive && v.Frame.Status == ProductStatuses.Active)
            .OrderBy(v => v.Sku)
            .FirstAsync();

        if (variant.StockQty < minStock)
        {
            variant.StockQty = minStock;
            await db.SaveChangesAsync();
        }

        return variant.Id;
    }

    public void Dispose() => _provider.Dispose();
}

/// <summary>
/// Stands in for the cart cookie.
///
/// A plain static field, deliberately not AsyncLocal: an AsyncLocal write inside
/// an async call flows *down* the call chain, not back up to the caller, so a
/// token minted inside CartService would be invisible to the next scope — the
/// exact symptom of a bag that "expired" between adding an item and checking out.
/// Tests in this collection run sequentially, so a shared field is safe.
/// </summary>
public sealed class InMemoryCartToken : ICartTokenAccessor
{
    private static string? _token;

    public string? Read() => _token;
    public void Write(string token) => _token = token;
    public void Clear() => _token = null;
    public static void Reset() => _token = null;
}

/// <summary>
/// Points the storage provider at a scratch directory under the test output, so
/// uploads land somewhere disposable instead of in the application's wwwroot.
/// </summary>
public sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public TestWebHostEnvironment()
    {
        WebRootPath = Path.Combine(AppContext.BaseDirectory, "test-wwwroot");
        Directory.CreateDirectory(WebRootPath);
        ContentRootPath = AppContext.BaseDirectory;
        WebRootFileProvider = new PhysicalFileProvider(WebRootPath);
        ContentRootFileProvider = new PhysicalFileProvider(ContentRootPath);
    }

    public string WebRootPath { get; set; }
    public IFileProvider WebRootFileProvider { get; set; }
    public string ApplicationName { get; set; } = "VisionCart.Tests";
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; }
    public string EnvironmentName { get; set; } = "Test";
}

[CollectionDefinition("checkout")]
public class CheckoutCollection : ICollectionFixture<CheckoutFlowFixture>;
