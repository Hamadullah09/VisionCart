using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Common;
using VisionCart.Application.Email;
using VisionCart.Application.Platform;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;

namespace VisionCart.Application.Privacy;

public sealed class DataRequestInput
{
    public string Kind { get; set; } = DataSubjectRequestKinds.Correction;
    public string Email { get; set; } = string.Empty;
    public string? Message { get; set; }
}

public sealed record ErasureImpact(
    int Orders, int Prescriptions, int Appointments, int Addresses, bool CanErase, string? Blocker);

public interface IDataSubjectService
{
    Task<ActionResult<DataSubjectRequest>> RaiseAsync(
        string? userId, DataRequestInput input, CancellationToken ct = default);

    Task<IReadOnlyList<DataSubjectRequest>> ForUserAsync(string userId, CancellationToken ct = default);
    Task<PagedResult<DataSubjectRequest>> QueueAsync(
        string? status, int page, CancellationToken ct = default);

    Task<DataSubjectRequest?> FindAsync(string id, CancellationToken ct = default);
    Task<ActionResult> SetStatusAsync(
        string id, string status, string? staffNotes, CancellationToken ct = default);

    Task<string> ExportPersonalDataAsync(string userId, CancellationToken ct = default);
    Task<ErasureImpact> AssessErasureAsync(string patientId, CancellationToken ct = default);
    Task<ActionResult> EraseAsync(string requestId, CancellationToken ct = default);
}

/// <summary>
/// Correction, export and erasure of personal data.
///
/// The hard part of erasure in a clinical shop is that it collides with two
/// obligations that outrank a deletion request: an optical prescription is a
/// medical record with a retention period, and an order is a financial record
/// that must survive for tax and consumer-protection purposes.
///
/// So erasure here is **pseudonymisation, not deletion**. Identity is destroyed
/// — name, email, phone, date of birth, address lines — while the clinical and
/// financial rows keep their shape, their totals and their foreign keys. What
/// remains cannot be traced back to a person; what a court or an auditor needs
/// is still there. A hard delete would either fail on the foreign keys or
/// silently shred a sales ledger.
///
/// Every path through this service is audited, and the audit detail never
/// carries the values being changed (§10).
/// </summary>
public sealed class DataSubjectService(
    IApplicationDbContext db,
    IAuditService audit,
    IEmailService email) : IDataSubjectService
{
    private const int PerPage = 25;

    /// <summary>Placeholder written over identifying columns on erasure.</summary>
    private const string Redacted = "[erased]";

    public async Task<ActionResult<DataSubjectRequest>> RaiseAsync(
        string? userId, DataRequestInput input, CancellationToken ct = default)
    {
        if (!DataSubjectRequestKinds.All.Contains(input.Kind))
            return ActionResult<DataSubjectRequest>.Fail("Choose what you would like us to do.");

        var address = (input.Email ?? string.Empty).Trim();
        if (address.Length == 0 || !address.Contains('@'))
            return ActionResult<DataSubjectRequest>.Fail("Enter the email address on your account.");

        // One open request per person per kind. Without this the queue fills with
        // duplicates from a customer clicking twice, and staff cannot tell which
        // one is live.
        var duplicate = await db.DataSubjectRequests.AnyAsync(
            r => r.Email == address
                 && r.Kind == input.Kind
                 && (r.Status == DataSubjectRequestStatuses.Pending
                     || r.Status == DataSubjectRequestStatuses.InReview), ct);

        if (duplicate)
            return ActionResult<DataSubjectRequest>.Fail(
                "We already have this request open and are working on it. We will be in touch.");

        var patient = await db.Patients
            .Where(p => p.Email == address)
            .Select(p => new { p.Id })
            .FirstOrDefaultAsync(ct);

        var request = new DataSubjectRequest
        {
            UserId = userId,
            PatientId = patient?.Id,
            Email = address,
            Kind = input.Kind,
            Status = DataSubjectRequestStatuses.Pending,
            CustomerMessage = string.IsNullOrWhiteSpace(input.Message) ? null : input.Message.Trim(),
        };

        db.DataSubjectRequests.Add(request);
        await db.SaveChangesAsync(ct);

        // Kind only. The message a customer writes may itself contain clinical
        // detail, so it stays in its column and out of the trail.
        await audit.WriteAsync(AuditActions.DataRequestRaise, "DataSubjectRequest", request.Id,
            new { request.Kind }, ct);

        await email.QueueAsync("data_request", address, null,
            "We have received your request",
            $"<p>We have logged your request to <strong>{Label(request.Kind)}</strong>.</p>" +
            "<p>We will respond within 30 days. If we need to confirm your identity first, " +
            "we will write to you.</p>",
            "DataSubjectRequest", request.Id, ct);

        return ActionResult<DataSubjectRequest>.Success(request);
    }

    public async Task<IReadOnlyList<DataSubjectRequest>> ForUserAsync(
        string userId, CancellationToken ct = default) =>
        await db.DataSubjectRequests
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(25)
            .ToListAsync(ct);

    public async Task<PagedResult<DataSubjectRequest>> QueueAsync(
        string? status, int page, CancellationToken ct = default)
    {
        page = Math.Max(1, page);

        var query = db.DataSubjectRequests.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && DataSubjectRequestStatuses.All.Contains(status))
            query = query.Where(r => r.Status == status);

        var total = await query.CountAsync(ct);

        var items = await query
            // Oldest first: a statutory clock is running on each of these, so the
            // one closest to its deadline must be the one staff see first.
            .OrderBy(r => r.Status == DataSubjectRequestStatuses.Completed
                          || r.Status == DataSubjectRequestStatuses.Rejected)
            .ThenBy(r => r.CreatedAt)
            .Skip((page - 1) * PerPage)
            .Take(PerPage)
            .ToListAsync(ct);

        return new PagedResult<DataSubjectRequest>
        {
            Items = items, Total = total, Page = page, PerPage = PerPage,
        };
    }

    public Task<DataSubjectRequest?> FindAsync(string id, CancellationToken ct = default) =>
        db.DataSubjectRequests.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<ActionResult> SetStatusAsync(
        string id, string status, string? staffNotes, CancellationToken ct = default)
    {
        if (!DataSubjectRequestStatuses.All.Contains(status))
            return ActionResult.Fail("That is not a valid status.");

        var request = await FindAsync(id, ct);
        if (request is null) return ActionResult.Fail("That request no longer exists.");

        request.Status = status;
        request.StaffNotes = string.IsNullOrWhiteSpace(staffNotes) ? request.StaffNotes : staffNotes.Trim();
        request.UpdatedAt = DateTime.UtcNow;

        if (status is DataSubjectRequestStatuses.Completed or DataSubjectRequestStatuses.Rejected)
            request.HandledAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditActions.DataRequestHandle, "DataSubjectRequest", request.Id,
            new { request.Kind, request.Status }, ct);

        if (status == DataSubjectRequestStatuses.Completed)
            await email.QueueAsync("data_request", request.Email, null,
                "Your request has been completed",
                $"<p>Your request to <strong>{Label(request.Kind)}</strong> has been completed.</p>",
                "DataSubjectRequest", request.Id, ct);

        return ActionResult.Success();
    }

    /// <summary>
    /// Everything held about one account, as JSON.
    ///
    /// Assembled by explicit projection rather than by serialising entities: an
    /// entity graph would drag in navigation properties and hand the customer
    /// other people's rows.
    /// </summary>
    public async Task<string> ExportPersonalDataAsync(string userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Email, u.Name, u.Role, u.PhoneNumber })
            .FirstOrDefaultAsync(ct);

        var patient = await db.Patients.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new
            {
                p.Id, p.FileNo, p.FirstName, p.LastName, p.Email, p.Phone,
                p.DateOfBirth, p.PdMm, p.ConsentMarketing, p.CreatedAt,
            })
            .FirstOrDefaultAsync(ct);

        var addresses = await db.Addresses.AsNoTracking()
            .Where(a => a.UserId == userId && a.DeletedAt == null)
            .Select(a => new
            {
                a.Label, a.FullName, a.Phone, a.Line1, a.Line2,
                a.City, a.State, a.PostalCode, a.Country, a.IsDefault,
            })
            .ToListAsync(ct);

        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.PlacedAt)
            .Select(o => new
            {
                o.Id, o.OrderNo, o.Status, o.PaymentStatus, o.PlacedAt,
                o.TotalMinor, o.Currency,
                Items = o.Items.Select(i => new
                {
                    i.TitleSnapshot, i.SkuSnapshot, i.Qty,
                    i.UnitPriceMinor, i.LensPriceMinor, i.TotalMinor, i.LensSummary,
                }).ToList(),
            })
            .ToListAsync(ct);

        var prescriptions = patient is null ? [] : await db.Prescriptions.AsNoTracking()
            .Where(p => p.PatientId == patient.Id)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id, p.Status, p.Source, p.IssuedAt, p.ExpiresAt, p.Prescriber,
                p.OdSphere, p.OdCylinder, p.OdAxis, p.OdAdd, p.OdPdMm,
                p.OsSphere, p.OsCylinder, p.OsAxis, p.OsAdd, p.OsPdMm,
                p.CreatedAt,
            })
            .ToListAsync(ct);

        var appointments = patient is null ? [] : await db.Appointments.AsNoTracking()
            .Where(a => a.PatientId == patient.Id)
            .OrderByDescending(a => a.StartsAt)
            .Select(a => new { a.StartsAt, a.Minutes, a.Kind, a.Status })
            .ToListAsync(ct);

        var payload = new
        {
            ExportedAt = DateTime.UtcNow,
            Account = user,
            PatientFile = patient,
            Addresses = addresses,
            Orders = orders,
            Prescriptions = prescriptions,
            Appointments = appointments,
        };

        // The export itself is a disclosure of clinical data, so it is recorded
        // the same way a staff-initiated patient export is.
        await audit.WriteAsync(AuditActions.ExportPatients, "ApplicationUser", userId,
            new { Scope = "self_service" }, ct);

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<ErasureImpact> AssessErasureAsync(string patientId, CancellationToken ct = default)
    {
        var patient = await db.Patients.AsNoTracking()
            .Where(p => p.Id == patientId)
            .Select(p => new { p.Id, p.UserId })
            .FirstOrDefaultAsync(ct);

        if (patient is null)
            return new ErasureImpact(0, 0, 0, 0, false, "That patient file no longer exists.");

        var orders = await db.Orders.CountAsync(o => o.PatientId == patientId, ct);
        var prescriptions = await db.Prescriptions.CountAsync(p => p.PatientId == patientId, ct);
        var appointments = await db.Appointments.CountAsync(a => a.PatientId == patientId, ct);
        var addresses = patient.UserId is null ? 0
            : await db.Addresses.CountAsync(a => a.UserId == patient.UserId && a.DeletedAt == null, ct);

        // An order still moving is the one case where erasure has to wait: the
        // courier needs a name and an address to deliver to.
        var openOrder = await db.Orders.AnyAsync(
            o => o.PatientId == patientId
                 && o.Status != OrderStatuses.Delivered
                 && o.Status != OrderStatuses.Cancelled
                 && o.Status != OrderStatuses.Refunded, ct);

        return new ErasureImpact(
            orders, prescriptions, appointments, addresses,
            CanErase: !openOrder,
            Blocker: openOrder
                ? "This customer has an order still in progress. Complete or cancel it first."
                : null);
    }

    /// <summary>
    /// Pseudonymises the person behind a request: identity is destroyed, the
    /// clinical and financial records keep their shape. See the class remarks.
    /// </summary>
    public async Task<ActionResult> EraseAsync(string requestId, CancellationToken ct = default)
    {
        var request = await FindAsync(requestId, ct);
        if (request is null) return ActionResult.Fail("That request no longer exists.");

        if (request.Kind != DataSubjectRequestKinds.Erasure)
            return ActionResult.Fail("Only an erasure request can be actioned this way.");

        if (request.Status == DataSubjectRequestStatuses.Completed)
            return ActionResult.Fail("That request has already been completed.");

        if (request.PatientId is null)
            return ActionResult.Fail(
                "No patient file is linked to this request. Link one before erasing.");

        var impact = await AssessErasureAsync(request.PatientId, ct);
        if (!impact.CanErase) return ActionResult.Fail(impact.Blocker ?? "This record cannot be erased yet.");

        // One transaction: a half-erased person — name gone, address still there —
        // is worse than either outcome.
        await db.ExecuteInTransactionAsync(async token =>
        {
            var patient = await db.Patients.FirstAsync(p => p.Id == request.PatientId, token);
            var userId = patient.UserId;

            patient.FirstName = Redacted;
            patient.LastName = string.Empty;
            patient.Email = null;
            patient.Phone = null;
            patient.DateOfBirth = null;
            patient.ConsentMarketing = false;
            patient.Notes = null;
            patient.UpdatedAt = DateTime.UtcNow;

            // The order keeps its own contact details for the courier and the
            // receipt, so redacting the patient file alone would leave the name
            // and email sitting on every past order.
            var orders = await db.Orders.Where(o => o.PatientId == patient.Id).ToListAsync(token);
            foreach (var order in orders)
            {
                order.Email = $"erased-{order.Id}@invalid.local";
                order.Phone = null;
                order.Notes = null;
            }

            if (userId is not null)
            {
                var addresses = await db.Addresses.Where(a => a.UserId == userId).ToListAsync(token);
                foreach (var address in addresses)
                {
                    address.FullName = Redacted;
                    address.Phone = null;
                    address.Line1 = Redacted;
                    address.Line2 = null;
                    address.PostalCode = null;
                    address.DeletedAt ??= DateTime.UtcNow;
                }

                var account = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, token);
                if (account is not null)
                {
                    // The account is retired rather than deleted: Identity rows are
                    // referenced by the audit trail, and deleting one would take a
                    // record of who did what with it.
                    account.Name = Redacted;
                    account.Email = $"erased-{account.Id}@invalid.local";
                    account.NormalizedEmail = account.Email.ToUpperInvariant();
                    account.UserName = account.Email;
                    account.NormalizedUserName = account.Email.ToUpperInvariant();
                    account.PhoneNumber = null;
                    account.IsActive = false;

                    // Invalidates every existing session for this account.
                    account.SecurityStamp = Guid.NewGuid().ToString("N");
                }
            }

            request.Status = DataSubjectRequestStatuses.Completed;
            request.HandledAt = DateTime.UtcNow;
            request.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(token);
        }, ct);

        // Recorded against the request, not the person — the identifier of someone
        // who asked to be forgotten does not belong in a permanent trail.
        await audit.WriteAsync(AuditActions.DataRequestErase, "DataSubjectRequest", request.Id,
            new { impact.Orders, impact.Prescriptions, impact.Appointments }, ct);

        return ActionResult.Success();
    }

    private static string Label(string kind) =>
        DataSubjectRequestKinds.Labels.TryGetValue(kind, out var label) ? label.ToLowerInvariant() : kind;
}
