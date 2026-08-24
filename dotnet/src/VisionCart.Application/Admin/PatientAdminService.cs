using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Common;
using VisionCart.Application.Email;
using VisionCart.Application.Patients;
using VisionCart.Application.Platform;
using VisionCart.Application.Prescriptions;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;

namespace VisionCart.Application.Admin;

public sealed class PatientFilters
{
    public string? Q { get; init; }
    public bool PendingRxOnly { get; init; }
    public int Page { get; init; } = 1;
    public int PerPage { get; init; } = 25;
}

public sealed class PatientRow
{
    public string Id { get; init; } = string.Empty;
    public string FileNo { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public double? PdMm { get; init; }
    public int Orders { get; init; }
    public int PendingPrescriptions { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class PatientDetails
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public double? PdMm { get; set; }
    public double? PdNearMm { get; set; }
    public string? Tags { get; set; }
    public string? Notes { get; set; }
    public bool ConsentMarketing { get; set; }
}

public interface IPatientAdminService
{
    Task<PagedResult<PatientRow>> ListAsync(PatientFilters filters, CancellationToken ct = default);
    Task<Patient?> GetAsync(string id, CancellationToken ct = default);
    Task<ActionResult<string>> CreateAsync(PatientDetails details, CancellationToken ct = default);
    Task<ActionResult> UpdateAsync(string id, PatientDetails details, CancellationToken ct = default);

    Task<ActionResult<string>> AddPrescriptionAsync(string patientId, PrescriptionInput rx,
        string source, CancellationToken ct = default);

    Task<ActionResult> VerifyAsync(string prescriptionId, string verifiedByUserId,
        CancellationToken ct = default);

    Task<ActionResult> RejectAsync(string prescriptionId, string? reason, CancellationToken ct = default);
}

/// <summary>
/// Patient files and prescriptions. Port of the clinical half of
/// <c>src/app/actions/admin.ts</c>.
///
/// Two rules are enforced here rather than trusted to the caller:
/// a prescription already used on an order is never edited (a new version is
/// created instead), and clinical values never reach the audit detail.
/// </summary>
public sealed class PatientAdminService(
    IApplicationDbContext db,
    IPatientService patients,
    IEmailService email,
    IAuditService audit,
    TimeProvider clock) : IPatientAdminService
{
    public async Task<PagedResult<PatientRow>> ListAsync(
        PatientFilters filters, CancellationToken ct = default)
    {
        var query = db.Patients.AsNoTracking().Where(p => p.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(filters.Q))
        {
            var term = filters.Q.Trim();
            query = query.Where(p =>
                EF.Functions.Like(p.FileNo, $"%{term}%")
                || EF.Functions.Like(p.FirstName, $"%{term}%")
                || EF.Functions.Like(p.LastName, $"%{term}%")
                || (p.Email != null && EF.Functions.Like(p.Email, $"%{term}%"))
                || (p.Phone != null && EF.Functions.Like(p.Phone, $"%{term}%")));
        }

        if (filters.PendingRxOnly)
            query = query.Where(p => p.Prescriptions.Any(r => r.Status == RxStatuses.PendingVerification));

        var total = await query.CountAsync(ct);
        var perPage = Math.Clamp(filters.PerPage, 1, 100);
        var page = Math.Max(1, filters.Page);

        var rows = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(p => new PatientRow
            {
                Id = p.Id,
                FileNo = p.FileNo,
                Name = p.FirstName + " " + p.LastName,
                Email = p.Email,
                Phone = p.Phone,
                PdMm = p.PdMm,
                CreatedAt = p.CreatedAt,
                Orders = p.Orders.Count,
                PendingPrescriptions =
                    p.Prescriptions.Count(r => r.Status == RxStatuses.PendingVerification),
            })
            .ToListAsync(ct);

        return new PagedResult<PatientRow> { Items = rows, Total = total, Page = page, PerPage = perPage };
    }

    public async Task<Patient?> GetAsync(string id, CancellationToken ct = default) =>
        await db.Patients
            .Include(p => p.Prescriptions.OrderByDescending(r => r.IssuedAt))
            .Include(p => p.Documents.OrderByDescending(d => d.CreatedAt))
            .Include(p => p.Orders.OrderByDescending(o => o.PlacedAt))
            .Include(p => p.TryOnSessions).ThenInclude(s => s.Snapshots).ThenInclude(s => s.Variant)
                .ThenInclude(v => v.Frame)
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct);

    public async Task<ActionResult<string>> CreateAsync(
        PatientDetails details, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(details.FirstName))
            return ActionResult<string>.Fail("Enter the patient's first name.");

        var patient = new Patient { FileNo = await patients.NextFileNoAsync(ct) };
        Apply(patient, details);

        db.Patients.Add(patient);
        await db.SaveChangesAsync(ct);

        // No clinical values in the detail — the log is read far more widely
        // than the record it describes.
        await audit.WriteAsync(AuditActions.PatientCreate, "Patient", patient.Id,
            new { fileNo = patient.FileNo }, ct);

        return ActionResult<string>.Success(patient.Id);
    }

    public async Task<ActionResult> UpdateAsync(
        string id, PatientDetails details, CancellationToken ct = default)
    {
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct);
        if (patient is null) return ActionResult.Fail("That patient file no longer exists.");

        if (string.IsNullOrWhiteSpace(details.FirstName))
            return ActionResult.Fail("Enter the patient's first name.");

        var consentChanged = patient.ConsentMarketing != details.ConsentMarketing;
        Apply(patient, details);

        if (consentChanged && details.ConsentMarketing)
        {
            patient.ConsentDataAt = clock.GetUtcNow().UtcDateTime;
            patient.ConsentVersion = "1";
        }

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.PatientUpdate, "Patient", patient.Id,
            new { fileNo = patient.FileNo, consentChanged }, ct);

        return ActionResult.Success();
    }

    private static void Apply(Patient patient, PatientDetails d)
    {
        patient.FirstName = d.FirstName.Trim();
        patient.LastName = (d.LastName ?? string.Empty).Trim();
        patient.Email = Blank(d.Email)?.ToLowerInvariant();
        patient.Phone = Blank(d.Phone);
        patient.DateOfBirth = d.DateOfBirth;
        patient.Gender = Blank(d.Gender);
        patient.PdMm = d.PdMm;
        patient.PdNearMm = d.PdNearMm;
        patient.Tags = Blank(d.Tags);
        patient.Notes = Blank(d.Notes);
        patient.ConsentMarketing = d.ConsentMarketing;
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<ActionResult<string>> AddPrescriptionAsync(
        string patientId, PrescriptionInput rx, string source, CancellationToken ct = default)
    {
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == patientId, ct);
        if (patient is null) return ActionResult<string>.Fail("That patient file no longer exists.");

        var validation = Rx.Validate(rx);
        if (!validation.IsValid) return ActionResult<string>.Fail(validation.Errors[0].Message);

        var flat = Rx.ToFlat(rx);

        // A new version, never an edit. The old prescription stays on file and
        // any order dispensed against it still reads correctly.
        var prescription = new Prescription
        {
            PatientId = patient.Id,
            Source = RxSources.All.Contains(source) ? source : RxSources.InStoreExam,
            // Entered by staff in the practice, so it starts verified; anything
            // the customer typed arrives as pending from checkout instead.
            Status = RxStatuses.Verified,
            IssuedAt = rx.IssuedAt ?? clock.GetUtcNow().UtcDateTime,
            ExpiresAt = rx.ExpiresAt,
            VerifiedAt = clock.GetUtcNow().UtcDateTime,
            OdSphere = flat.OdSphere, OdCylinder = flat.OdCylinder, OdAxis = flat.OdAxis,
            OdAdd = flat.OdAdd, OdPrism = flat.OdPrism, OdPrismBase = flat.OdPrismBase,
            OdPdMm = flat.OdPdMm, OdSegHeightMm = flat.OdSegHeightMm,
            OsSphere = flat.OsSphere, OsCylinder = flat.OsCylinder, OsAxis = flat.OsAxis,
            OsAdd = flat.OsAdd, OsPrism = flat.OsPrism, OsPrismBase = flat.OsPrismBase,
            OsPdMm = flat.OsPdMm, OsSegHeightMm = flat.OsSegHeightMm,
            Prescriber = flat.Prescriber, Clinic = flat.Clinic, Notes = flat.Notes,
        };

        db.Prescriptions.Add(prescription);

        // The binocular PD belongs to the person, not to one prescription.
        if (rx.PdMm is { } pd) patient.PdMm = pd;
        if (rx.PdNearMm is { } near) patient.PdNearMm = near;

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.PrescriptionCreate, "Prescription", prescription.Id,
            new { fileNo = patient.FileNo, source = prescription.Source }, ct);

        return ActionResult<string>.Success(prescription.Id);
    }

    public async Task<ActionResult> VerifyAsync(
        string prescriptionId, string verifiedByUserId, CancellationToken ct = default)
    {
        var prescription = await db.Prescriptions
            .Include(p => p.Patient)
            .FirstOrDefaultAsync(p => p.Id == prescriptionId, ct);

        if (prescription is null) return ActionResult.Fail("That prescription no longer exists.");
        if (prescription.Status == RxStatuses.Verified)
            return ActionResult.Fail("That prescription is already verified.");

        prescription.Status = RxStatuses.Verified;
        prescription.VerifiedBy = verifiedByUserId;
        prescription.VerifiedAt = clock.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.PrescriptionVerify, "Prescription", prescription.Id,
            new { fileNo = prescription.Patient.FileNo }, ct);

        await email.QueuePrescriptionVerifiedAsync(prescription.Id, ct);
        return ActionResult.Success();
    }

    public async Task<ActionResult> RejectAsync(
        string prescriptionId, string? reason, CancellationToken ct = default)
    {
        var prescription = await db.Prescriptions
            .Include(p => p.Patient)
            .FirstOrDefaultAsync(p => p.Id == prescriptionId, ct);

        if (prescription is null) return ActionResult.Fail("That prescription no longer exists.");

        prescription.Status = RxStatuses.Rejected;
        prescription.VerifiedAt = clock.GetUtcNow().UtcDateTime;

        if (!string.IsNullOrWhiteSpace(reason))
        {
            prescription.Notes = string.IsNullOrWhiteSpace(prescription.Notes)
                ? reason.Trim()
                : prescription.Notes + "\n\n" + reason.Trim();
        }

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.PrescriptionReject, "Prescription", prescription.Id,
            new { fileNo = prescription.Patient.FileNo }, ct);

        await email.QueuePrescriptionRejectedAsync(prescription.Id, reason, ct);
        return ActionResult.Success();
    }
}
