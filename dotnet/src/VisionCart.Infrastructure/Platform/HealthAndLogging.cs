using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisionCart.Infrastructure.Logging;
using VisionCart.Infrastructure.Persistence;

namespace VisionCart.Infrastructure.Platform;

/// <summary>
/// Confirms the database is reachable and migrated.
///
/// "The site returns 200" is not the same as "the site works": the most common
/// shared-hosting failure is an application that starts perfectly and cannot
/// reach SQL Server, which looks healthy until a customer tries to buy
/// something. This check answers the question that actually matters.
/// </summary>
public sealed class DatabaseHealthCheck(ApplicationDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(ct))
                return HealthCheckResult.Unhealthy("The database cannot be reached.");

            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

            // Degraded, not unhealthy: the site still serves, but a deployment
            // has half-landed and somebody needs to know before it bites.
            return pending.Count > 0
                ? HealthCheckResult.Degraded($"{pending.Count} migration(s) have not been applied.")
                : HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            // The message, never the connection string — it carries credentials.
            return HealthCheckResult.Unhealthy("The database check failed.", ex);
        }
    }
}

/// <summary>Reports whether outbound mail is backing up.</summary>
public sealed class EmailOutboxHealthCheck(ApplicationDbContext db) : IHealthCheck
{
    /// <summary>Above this, mail is not being delivered and orders go unconfirmed.</summary>
    private const int StuckThreshold = 50;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var stuck = await db.OutboxEmails.CountAsync(
                e => e.SentAt == null && e.Attempts > 0, ct);

            return stuck >= StuckThreshold
                ? HealthCheckResult.Degraded($"{stuck} emails have failed to send.")
                : HealthCheckResult.Healthy();
        }
        catch
        {
            // The database check already reports connectivity; do not report the
            // same fault twice and page somebody for one problem two ways.
            return HealthCheckResult.Healthy();
        }
    }
}

public static class PlatformServiceCollectionExtensions
{
    public static IServiceCollection AddVisionCartHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
            .AddCheck<EmailOutboxHealthCheck>("email-outbox", tags: ["ready"]);

        return services;
    }

    /// <summary>
    /// Adds the rolling file log. Console logging stays registered for local
    /// development; on IIS it simply has nowhere to go.
    /// </summary>
    public static ILoggingBuilder AddVisionCartFileLog(
        this ILoggingBuilder logging, IHostEnvironment environment)
    {
        logging.Services.AddSingleton<ILoggerProvider>(sp =>
            new FileLoggerProvider(
                sp.GetRequiredService<IOptions<FileLogOptions>>(),
                environment.ContentRootPath));

        return logging;
    }
}
