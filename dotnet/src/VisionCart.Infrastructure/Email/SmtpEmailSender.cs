using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisionCart.Application.Common;
using VisionCart.Application.Email;
using VisionCart.Domain.Entities;
using VisionCart.Infrastructure.Persistence;

namespace VisionCart.Infrastructure.Email;

/// <summary>Sends through SMTP. Credentials come from configuration, never from code.</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public string Name => "smtp";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Host);

    public async Task SendAsync(OutboxEmail message, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("SMTP is selected but Email:Host is not configured.");

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            client.Credentials = new NetworkCredential(_options.Username, _options.Password);
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true,
        };

        mail.To.Add(string.IsNullOrWhiteSpace(message.ToName)
            ? new MailAddress(message.ToAddress)
            : new MailAddress(message.ToAddress, message.ToName));

        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.TextBody, null, "text/plain"));
        }

        await client.SendMailAsync(mail, ct);
    }
}

/// <summary>
/// Writes the message to the log instead of sending it.
///
/// The default, and what makes a fresh install demonstrable without an SMTP
/// account: the whole ordering flow works and every email is visible in the log.
/// It is always "configured", so mail is never silently dropped.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public string Name => "log";
    public bool IsConfigured => true;

    public Task SendAsync(OutboxEmail message, CancellationToken ct = default)
    {
        logger.LogInformation(
            "EMAIL [{Template}] to {To}: {Subject}\n{Body}",
            message.Template, message.ToAddress, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Drains the email outbox.
///
/// Runs inside the web application's own process as an <see cref="IHostedService"/>
/// — deliberately, because shared IIS hosting cannot run a separate worker. The
/// cost is that mail only flows while the application pool is warm; a shop that
/// needs guaranteed delivery during idle periods should either enable Always On
/// or move this to a scheduled task hitting a drain endpoint.
///
/// Failures back off exponentially and give up after the configured attempt
/// limit, so one permanently-bad address cannot spin forever.
/// </summary>
public sealed class EmailOutboxService(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IEmailSender> senders,
    IOptions<EmailOptions> options,
    TimeProvider clock,
    ILogger<EmailOutboxService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);
    private const int BatchSize = 20;

    private readonly EmailOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the application finish starting before touching the database.
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The loop must survive anything, including the database being
                // briefly unreachable.
                logger.LogError(ex, "Email outbox drain failed; will retry");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        var sender = senders.FirstOrDefault(s =>
            string.Equals(s.Name, _options.Driver, StringComparison.OrdinalIgnoreCase))
            ?? senders.First(s => s.Name == "log");

        if (!sender.IsConfigured)
        {
            logger.LogWarning(
                "Email driver {Driver} is selected but not configured; mail is queued and waiting",
                sender.Name);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = clock.GetUtcNow().UtcDateTime;

        var batch = await db.OutboxEmails
            .Where(e => e.Status == "pending"
                        && (e.NextAttemptAt == null || e.NextAttemptAt <= now))
            .OrderBy(e => e.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0) return;

        foreach (var message in batch)
        {
            try
            {
                await sender.SendAsync(message, ct);
                message.Status = "sent";
                message.SentAt = clock.GetUtcNow().UtcDateTime;
                message.LastError = null;
            }
            catch (Exception ex)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 512 ? ex.Message[..512] : ex.Message;

                if (message.Attempts >= _options.MaxAttempts)
                {
                    message.Status = "abandoned";
                    logger.LogError(ex,
                        "Giving up on {Template} to {To} after {Attempts} attempts",
                        message.Template, message.ToAddress, message.Attempts);
                }
                else
                {
                    // 1, 2, 4, 8… minutes.
                    var delay = TimeSpan.FromMinutes(Math.Pow(2, message.Attempts - 1));
                    message.NextAttemptAt = clock.GetUtcNow().UtcDateTime.Add(delay);
                    logger.LogWarning(ex,
                        "Failed to send {Template} to {To}; retry {Attempt} in {Delay}",
                        message.Template, message.ToAddress, message.Attempts, delay);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
