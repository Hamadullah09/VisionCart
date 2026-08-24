using System.Text.Json;
using Microsoft.Extensions.Logging;
using VisionCart.Application.Common;
using VisionCart.Domain.Entities;

namespace VisionCart.Application.Platform;

/// <summary>
/// Carries the acting user and their client details into the audit trail.
/// Implemented in the web layer from <c>HttpContext</c>; a null implementation
/// is used by the seeder and by background work, which have no request.
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string? Email { get; }
    string? Ip { get; }
    string? UserAgent { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
}

public interface IAuditService
{
    Task WriteAsync(string action, string entity, string? entityId = null, object? detail = null,
        CancellationToken ct = default);
}

/// <summary>
/// Port of <c>src/lib/audit.ts</c>.
///
/// Every write to a patient record, order or price goes through here. Health
/// data needs a defensible answer to "who changed this, and when".
/// </summary>
public sealed class AuditService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ILogger<AuditService> logger) : IAuditService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public async Task WriteAsync(
        string action, string entity, string? entityId = null, object? detail = null,
        CancellationToken ct = default)
    {
        try
        {
            db.AuditLogs.Add(new AuditLog
            {
                UserId = currentUser.UserId,
                ActorEmail = currentUser.Email,
                Action = action,
                Entity = entity,
                EntityId = entityId,
                Detail = detail is null ? null : JsonSerializer.Serialize(detail, Json),
                Ip = currentUser.Ip,
                UserAgent = Truncate(currentUser.UserAgent, 512),
            });

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // An audit failure must never take down the operation it was
            // recording. The legacy implementation made the same choice, and it
            // is the right one: losing a sale to protect a log entry is worse
            // than losing the log entry.
            logger.LogError(ex, "Failed to write audit entry {Action} on {Entity} {EntityId}",
                action, entity, entityId);
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}

/// <summary>Used by the seeder, background services and tests — no request in scope.</summary>
public sealed class SystemUser : ICurrentUser
{
    public string? UserId => null;
    public string? Email => "system";
    public string? Ip => null;
    public string? UserAgent => null;
    public bool IsAuthenticated => false;
    public bool IsInRole(string role) => false;
}

/// <summary>
/// Audit action names. Kept as constants so the new audit viewer can offer a
/// filter list, and so a typo cannot silently create an unfindable category.
/// </summary>
public static class AuditActions
{
    public const string OrderPlace = "order.place";
    public const string OrderUpdate = "order.update";
    public const string OrderCancel = "order.cancel";
    public const string PaymentMarkPaid = "payment.mark_paid";
    public const string PaymentFailed = "payment.failed";
    public const string PaymentRefund = "payment.refund";
    public const string PatientCreate = "patient.create";
    public const string PatientUpdate = "patient.update";
    public const string PrescriptionCreate = "prescription.create";
    public const string PrescriptionVerify = "prescription.verify";
    public const string PrescriptionReject = "prescription.reject";
    public const string PriceUpdate = "price.update";
    public const string SettingsUpdate = "settings.update";
    public const string ExportPatients = "export.patients";
    public const string TryOnSnapshotSave = "tryon.snapshot.save";
    public const string AuthLogin = "auth.login";
    public const string AuthLoginFailed = "auth.login_failed";
    public const string AuthPasswordReset = "auth.password_reset";

    public const string AddressCreate = "address.create";
    public const string AddressUpdate = "address.update";
    public const string AddressDelete = "address.delete";

    public const string AppointmentBook = "appointment.book";
    public const string AppointmentReschedule = "appointment.reschedule";
    public const string AppointmentCancel = "appointment.cancel";
    public const string AppointmentComplete = "appointment.complete";

    public const string DataRequestRaise = "data_request.raise";
    public const string DataRequestHandle = "data_request.handle";
    public const string DataRequestErase = "data_request.erase";
}
