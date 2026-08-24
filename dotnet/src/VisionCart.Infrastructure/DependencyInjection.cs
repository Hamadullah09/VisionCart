using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VisionCart.Application.Admin;
using VisionCart.Application.Accounts;
using VisionCart.Application.Appointments;
using VisionCart.Application.Privacy;
using VisionCart.Application.Carts;
using VisionCart.Application.DataTransfer;
using VisionCart.Application.Media;
using VisionCart.Application.Catalogue;
using VisionCart.Application.Checkout;
using VisionCart.Application.Common;
using VisionCart.Application.Patients;
using VisionCart.Application.Payments;
using VisionCart.Application.Platform;
using VisionCart.Application.Pricing;
using StoreOptions = VisionCart.Application.Pricing.StoreOptions;
using VisionCart.Application.Promotions;
using VisionCart.Application.Shipping;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;
using VisionCart.Application.Email;
using VisionCart.Infrastructure.Email;
using VisionCart.Infrastructure.Payments;
using VisionCart.Infrastructure.Persistence;
using VisionCart.Infrastructure.Shipping;
using VisionCart.Application.Storage;
using VisionCart.Infrastructure.Storage;

namespace VisionCart.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddVisionCartInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // --- Options ---------------------------------------------------------
        services.Configure<StoreOptions>(configuration.GetSection(StoreOptions.SectionName));
        services.Configure<TaxOptions>(configuration.GetSection(TaxOptions.SectionName));
        services.Configure<PaymentOptions>(configuration.GetSection(PaymentOptions.SectionName));
        services.Configure<ShippingOptions>(configuration.GetSection(ShippingOptions.SectionName));
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));

        // --- Database --------------------------------------------------------
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured. " +
                "Set it in appsettings or as the ConnectionStrings__DefaultConnection environment variable.");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                // Shared hosting shares a SQL Server instance with other tenants;
                // a transient timeout must retry rather than surface as a 500.
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null);
            });
            options.AddInterceptors(new TimestampInterceptor());
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<DatabaseSeeder>();

        // --- Identity --------------------------------------------------------
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = false;

                // Replaces bcryptjs. Identity's hasher is PBKDF2 with 100k
                // iterations by default in .NET 10.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                // The legacy system had no lockout at all — a password could be
                // brute-forced indefinitely.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // Single-use, expiring password reset tokens — the mechanism the new
        // recovery flow is built on. Six hours is long enough for someone to
        // find the email and short enough to limit exposure.
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(6));

        // --- Domain services -------------------------------------------------
        services.AddSingleton(TimeProvider.System);
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
        services.AddScoped<IStorageProvider, LocalStorageProvider>();
        services.AddScoped<IEmailService, EmailService>();

        // --- Back office -----------------------------------------------------
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IOrderAdminService, OrderAdminService>();
        services.AddScoped<IPatientAdminService, PatientAdminService>();
        services.AddScoped<ICatalogueAdminService, CatalogueAdminService>();
        services.AddScoped<IPlatformAdminService, PlatformAdminService>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IExportService, ExportService>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IAddressService, AddressService>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<IDataSubjectService, DataSubjectService>();
        services.AddHostedService<MediaPurgeService>();

        // Both senders are registered; the outbox picks the configured one and
        // falls back to the logging sender so mail is never silently dropped.
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<IEmailSender, LoggingEmailSender>();
        services.AddHostedService<EmailOutboxService>();

        // --- Provider adapters -----------------------------------------------
        // Registered as a set; the service picks the configured one by name and
        // falls back safely when it is absent or failing.
        services.AddScoped<IPaymentProvider, CashOnDeliveryProvider>();
        services.AddScoped<IPaymentProvider, BankTransferProvider>();
        services.AddScoped<IPaymentProvider, StripePaymentProvider>();

        services.AddHttpClient<IShippingProvider, ShippoShippingProvider>(client =>
            client.Timeout = TimeSpan.FromSeconds(12));
        services.AddHttpClient<IShippingProvider, EasyPostShippingProvider>(client =>
            client.Timeout = TimeSpan.FromSeconds(12));

        return services;
    }

    /// <summary>
    /// Applies migrations and seeds. Called at startup so a deployment needs no
    /// separate migration step on shared hosting, where running a CLI is awkward.
    /// </summary>
    public static async Task InitialiseDatabaseAsync(this IServiceProvider services, string webRootPath)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        var seedOptions = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<SeedOptions>>().Value;

        if (!seedOptions.Enabled) return;

        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync(seedOptions, webRootPath);
    }
}
