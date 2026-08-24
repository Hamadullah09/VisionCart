using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VisionCart.Application.Media;

namespace VisionCart.Infrastructure.Storage;

/// <summary>
/// Retries storage deletions that failed at the time of removal.
///
/// This is the second half of the cloud-orphan fix. The legacy implementation
/// deleted the database row and left the object in the bucket forever; now a
/// failed delete stays visible as a pending purge and is retried here until it
/// succeeds or exhausts its attempts.
///
/// In-process, like the email outbox, because shared IIS hosting cannot run a
/// separate worker. Hourly is ample — nothing is waiting on it.
/// </summary>
public sealed class MediaPurgeService(
    IServiceScopeFactory scopeFactory,
    ILogger<MediaPurgeService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the application finish starting before touching the database.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var media = scope.ServiceProvider.GetRequiredService<IMediaService>();
                await media.PurgePendingAsync(ct: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The loop must survive anything, including the database being
                // briefly unreachable.
                logger.LogError(ex, "Media purge sweep failed; will retry");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
