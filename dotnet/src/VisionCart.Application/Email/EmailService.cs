using System.Text.Encodings.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisionCart.Application.Common;
using VisionCart.Application.Platform;
using VisionCart.Application.Pricing;
using VisionCart.Domain.Entities;
using VisionCart.Domain.ValueObjects;

namespace VisionCart.Application.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>smtp | log — `log` writes the message to the logger instead of sending.</summary>
    public string Driver { get; set; } = "log";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseSsl { get; set; } = true;
    public string FromAddress { get; set; } = "orders@example.com";
    public string FromName { get; set; } = "VisionCart Optical";

    /// <summary>Give up after this many failures and stop retrying.</summary>
    public int MaxAttempts { get; set; } = 6;
}

/// <summary>Sends one message. Implemented in Infrastructure.</summary>
public interface IEmailSender
{
    string Name { get; }
    bool IsConfigured { get; }
    Task SendAsync(OutboxEmail message, CancellationToken ct = default);
}

public interface IEmailService
{
    /// <summary>Queues a message. Never sends inline — see the class remarks.</summary>
    Task QueueAsync(string template, string toAddress, string? toName, string subject,
        string htmlBody, string? relatedEntity = null, string? relatedEntityId = null,
        CancellationToken ct = default);

    Task QueueOrderConfirmationAsync(string orderId, CancellationToken ct = default);
    Task QueuePaymentConfirmationAsync(string orderId, CancellationToken ct = default);
    Task QueueOrderStatusAsync(string orderId, string newStatus, CancellationToken ct = default);
    Task QueueShipmentAsync(string orderId, string shipmentId, CancellationToken ct = default);
    Task QueuePrescriptionVerifiedAsync(string prescriptionId, CancellationToken ct = default);
    Task QueuePrescriptionRejectedAsync(string prescriptionId, string? reason, CancellationToken ct = default);
    Task QueuePasswordResetAsync(string email, string name, string resetUrl, CancellationToken ct = default);
}

/// <summary>
/// Closes the largest gap in the legacy system: it had a mail setting in its
/// configuration and not one line of code that sent anything, so a customer who
/// ordered received no confirmation at all.
///
/// Mail is <b>queued, never sent inline</b>. A slow or unreachable SMTP server
/// must not be able to stall checkout, and a failed order confirmation must be
/// retried rather than lost. <see cref="OutboxEmail"/> rows are drained by a
/// hosted service inside the same worker process — no external worker, which
/// keeps the deployment inside what shared IIS hosting supports.
/// </summary>
public sealed class EmailService(
    IApplicationDbContext db,
    ISettingsService settings,
    IOptions<StoreOptions> store,
    TimeProvider clock,
    ILogger<EmailService> logger) : IEmailService
{
    private readonly StoreOptions _store = store.Value;

    public async Task QueueAsync(
        string template, string toAddress, string? toName, string subject, string htmlBody,
        string? relatedEntity = null, string? relatedEntityId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toAddress))
        {
            logger.LogWarning("Refusing to queue {Template}: no recipient address", template);
            return;
        }

        db.OutboxEmails.Add(new OutboxEmail
        {
            Template = template,
            ToAddress = toAddress,
            ToName = toName,
            Subject = subject,
            HtmlBody = htmlBody,
            TextBody = EmailTemplates.ToPlainText(htmlBody),
            Status = "pending",
            NextAttemptAt = clock.GetUtcNow().UtcDateTime,
            RelatedEntity = relatedEntity,
            RelatedEntityId = relatedEntityId,
        });

        await db.SaveChangesAsync(ct);
    }

    private async Task<EmailTemplates.Branding> BrandingAsync(CancellationToken ct)
    {
        var all = await settings.GetAllAsync(ct);
        return new EmailTemplates.Branding
        {
            StoreName = all.GetValueOrDefault(SettingKeys.StoreName, _store.Name),
            StoreEmail = all.GetValueOrDefault(SettingKeys.StoreEmail, ""),
            StorePhone = all.GetValueOrDefault(SettingKeys.StorePhone, ""),
            AppUrl = _store.AppUrl.TrimEnd('/'),
            Currency = _store.Currency,
            CurrencySymbol = _store.CurrencySymbol,
        };
    }

    public async Task QueueOrderConfirmationAsync(string orderId, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return;

        var brand = await BrandingAsync(ct);
        await QueueAsync("order.confirmation", order.Email, null,
            $"Your order {order.OrderNo}",
            EmailTemplates.OrderConfirmation(order, brand), "Order", order.Id, ct);
    }

    public async Task QueuePaymentConfirmationAsync(string orderId, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return;

        var brand = await BrandingAsync(ct);
        await QueueAsync("payment.confirmation", order.Email, null,
            $"Payment received for {order.OrderNo}",
            EmailTemplates.PaymentConfirmation(order, brand), "Order", order.Id, ct);
    }

    public async Task QueueOrderStatusAsync(string orderId, string newStatus, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return;

        var brand = await BrandingAsync(ct);
        await QueueAsync($"order.{newStatus}", order.Email, null,
            $"Update on your order {order.OrderNo}",
            EmailTemplates.OrderStatus(order, newStatus, brand), "Order", order.Id, ct);
    }

    public async Task QueueShipmentAsync(string orderId, string shipmentId, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        var shipment = await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shipmentId, ct);
        if (order is null || shipment is null) return;

        var brand = await BrandingAsync(ct);
        await QueueAsync("order.shipped", order.Email, null,
            $"Your order {order.OrderNo} is on its way",
            EmailTemplates.Shipment(order, shipment, brand), "Order", order.Id, ct);
    }

    public async Task QueuePrescriptionVerifiedAsync(string prescriptionId, CancellationToken ct = default)
    {
        var (email, name, brand) = await PrescriptionRecipientAsync(prescriptionId, ct);
        if (email is null) return;

        await QueueAsync("prescription.verified", email, name,
            "Your prescription has been checked",
            EmailTemplates.PrescriptionVerified(name, brand), "Prescription", prescriptionId, ct);
    }

    public async Task QueuePrescriptionRejectedAsync(
        string prescriptionId, string? reason, CancellationToken ct = default)
    {
        var (email, name, brand) = await PrescriptionRecipientAsync(prescriptionId, ct);
        if (email is null) return;

        await QueueAsync("prescription.rejected", email, name,
            "We need to check your prescription",
            EmailTemplates.PrescriptionRejected(name, reason, brand), "Prescription", prescriptionId, ct);
    }

    public async Task QueuePasswordResetAsync(
        string email, string name, string resetUrl, CancellationToken ct = default)
    {
        var brand = await BrandingAsync(ct);
        await QueueAsync("auth.password_reset", email, name,
            "Reset your password",
            EmailTemplates.PasswordReset(name, resetUrl, brand), "User", null, ct);
    }

    private async Task<(string? Email, string Name, EmailTemplates.Branding Brand)>
        PrescriptionRecipientAsync(string prescriptionId, CancellationToken ct)
    {
        var brand = await BrandingAsync(ct);

        var patient = await db.Prescriptions.AsNoTracking()
            .Where(p => p.Id == prescriptionId)
            .Select(p => new { p.Patient.Email, p.Patient.FirstName })
            .FirstOrDefaultAsync(ct);

        return (patient?.Email, patient?.FirstName ?? "there", brand);
    }
}

/// <summary>
/// Reusable templates. Deliberately plain, table-free HTML with inline styles —
/// what mail clients actually render reliably.
///
/// No clinical values appear in any of them: an email is the least controlled
/// place a prescription could end up.
/// </summary>
public static class EmailTemplates
{
    public sealed class Branding
    {
        public string StoreName { get; init; } = "VisionCart Optical";
        public string StoreEmail { get; init; } = string.Empty;
        public string StorePhone { get; init; } = string.Empty;
        public string AppUrl { get; init; } = string.Empty;
        public string Currency { get; init; } = "PKR";
        public string CurrencySymbol { get; init; } = "Rs.";
    }

    private static string E(string? value) => HtmlEncoder.Default.Encode(value ?? string.Empty);

    private static string Layout(Branding brand, string heading, string body) => $"""
        <div style="font-family:system-ui,-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;
                    color:#1a2230;line-height:1.55;max-width:600px;margin:0 auto;padding:24px;">
          <p style="font-size:18px;font-weight:700;margin:0 0 24px;">{E(brand.StoreName)}</p>
          <h1 style="font-size:22px;margin:0 0 16px;">{heading}</h1>
          {body}
          <hr style="border:0;border-top:1px solid #eaeef4;margin:28px 0 16px;" />
          <p style="font-size:12px;color:#5b6472;margin:0;">
            {E(brand.StoreName)}{(string.IsNullOrWhiteSpace(brand.StorePhone) ? "" : " · " + E(brand.StorePhone))}
            {(string.IsNullOrWhiteSpace(brand.StoreEmail) ? "" : " · " + E(brand.StoreEmail))}
          </p>
        </div>
        """;

    private static string Money_(int minor, Branding b) =>
        Money.Format(minor, b.Currency, b.CurrencySymbol);

    public static string OrderConfirmation(Order order, Branding brand)
    {
        var lines = string.Join("", order.Items.Select(i => $"""
            <p style="margin:0 0 6px;">
              <strong>{E(i.TitleSnapshot)}</strong>{(i.Qty > 1 ? $" &times; {i.Qty}" : "")}<br />
              <span style="color:#5b6472;font-size:14px;">{E(i.LensSummary)}</span><br />
              <span style="font-size:14px;">{Money_(i.TotalMinor, brand)}</span>
            </p>
            """));

        var rx = order.Items.Any(i => i.PrescriptionId is not null)
            ? """
              <p style="background:#fdf6e3;border:1px solid #ecdcb0;border-radius:8px;padding:12px;font-size:14px;">
                One of our opticians will check your prescription before your lenses are cut.
                We'll email you when that's done.
              </p>
              """
            : "";

        return Layout(brand, $"Thank you — order {E(order.OrderNo)}", $"""
            <p>We've got your order and we're getting started on it.</p>
            {lines}
            <p style="border-top:1px solid #eaeef4;padding-top:12px;margin-top:16px;">
              <strong>Total: {Money_(order.TotalMinor, brand)}</strong>
            </p>
            {rx}
            <p><a href="{E(brand.AppUrl)}/order/{E(order.OrderNo)}"
                  style="display:inline-block;background:#0b5fa5;color:#fff;padding:10px 20px;
                         border-radius:8px;text-decoration:none;font-weight:600;">View your order</a></p>
            """);
    }

    public static string PaymentConfirmation(Order order, Branding brand) =>
        Layout(brand, "Payment received", $"""
            <p>Thank you — we've received your payment of
               <strong>{Money_(order.TotalMinor, brand)}</strong> for order
               <strong>{E(order.OrderNo)}</strong>.</p>
            <p>Your order is now with our lab.</p>
            <p><a href="{E(brand.AppUrl)}/order/{E(order.OrderNo)}">Track your order</a></p>
            """);

    public static string OrderStatus(Order order, string status, Branding brand)
    {
        var message = status switch
        {
            "in_lab" => "Your lenses are being made. This usually takes 3–5 working days.",
            "ready" => "Your glasses are ready and will be dispatched shortly.",
            "delivered" => "Your glasses have been delivered. We hope you love them.",
            "cancelled" => "Your order has been cancelled. Any payment taken will be refunded.",
            "refunded" => "Your refund has been issued and should reach you within a few working days.",
            _ => "There's an update on your order.",
        };

        return Layout(brand, $"Order {E(order.OrderNo)}", $"""
            <p>{E(message)}</p>
            <p><a href="{E(brand.AppUrl)}/order/{E(order.OrderNo)}">View your order</a></p>
            """);
    }

    public static string Shipment(Order order, Shipment shipment, Branding brand)
    {
        var tracking = string.IsNullOrWhiteSpace(shipment.TrackingNumber)
            ? ""
            : $"""
               <p style="font-size:14px;">
                 {E(shipment.Carrier.ToUpperInvariant())} — {E(shipment.TrackingNumber)}
                 {(string.IsNullOrWhiteSpace(shipment.TrackingUrl)
                     ? ""
                     : $"<br /><a href=\"{E(shipment.TrackingUrl)}\">Track this parcel</a>")}
               </p>
               """;

        return Layout(brand, "Your glasses are on their way", $"""
            <p>Order <strong>{E(order.OrderNo)}</strong> has been dispatched.</p>
            {tracking}
            <p><a href="{E(brand.AppUrl)}/order/{E(order.OrderNo)}">View your order</a></p>
            """);
    }

    public static string PrescriptionVerified(string name, Branding brand) =>
        Layout(brand, "Your prescription has been checked", $"""
            <p>Hello {E(name)},</p>
            <p>One of our opticians has checked your prescription and it's all in order.
               Your lenses are going into production.</p>
            """);

    public static string PrescriptionRejected(string name, string? reason, Branding brand) =>
        Layout(brand, "We need to check your prescription", $"""
            <p>Hello {E(name)},</p>
            <p>Our optician has looked at the prescription on your order and needs to
               check something with you before we cut your lenses.</p>
            {(string.IsNullOrWhiteSpace(reason)
                ? ""
                : $"<p style=\"background:#f4f6fa;border-radius:8px;padding:12px;\">{E(reason)}</p>")}
            <p>Please reply to this email or call us and we'll sort it out quickly.
               Nothing has been made yet, so there's no cost to correcting it.</p>
            """);

    public static string PasswordReset(string name, string resetUrl, Branding brand) =>
        Layout(brand, "Reset your password", $"""
            <p>Hello {E(name)},</p>
            <p>Someone asked to reset the password for this email address. If that was
               you, use the button below. The link works once and expires in six hours.</p>
            <p><a href="{E(resetUrl)}"
                  style="display:inline-block;background:#0b5fa5;color:#fff;padding:10px 20px;
                         border-radius:8px;text-decoration:none;font-weight:600;">Reset my password</a></p>
            <p style="font-size:14px;color:#5b6472;">
              If it wasn't you, you can ignore this email — your password has not changed.
            </p>
            """);

    /// <summary>Crude but adequate plain-text alternative for clients that want one.</summary>
    public static string ToPlainText(string html)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<br\\s*/?>", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, "</p>|</div>|</h1>", "\n\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);
        text = System.Text.RegularExpressions.Regex.Replace(text, "[ \\t]+", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, "\n{3,}", "\n\n");
        return text.Trim();
    }
}
