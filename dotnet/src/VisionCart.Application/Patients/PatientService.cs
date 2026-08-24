using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Common;
using VisionCart.Domain.Entities;

namespace VisionCart.Application.Patients;

public interface IPatientService
{
    Task<string> NextFileNoAsync(CancellationToken ct = default);
    Task<Patient> EnsureForUserAsync(string userId, CancellationToken ct = default);
    Task<Patient> FindOrCreateGuestAsync(string email, string? phone, string fullName, CancellationToken ct = default);
}

/// <summary>
/// Port of the patient-file helpers in <c>src/lib/auth.ts</c>.
///
/// Every customer gets a patient file from day one — the shop is an optical
/// practice, so the clinical record is the primary entity, not an add-on. Guests
/// get one too: an optical order cannot be remade or followed up without one.
/// </summary>
public sealed class PatientService(IApplicationDbContext db) : IPatientService
{
    /// <summary>Next sequential patient file number, e.g. P-000042.</summary>
    public async Task<string> NextFileNoAsync(CancellationToken ct = default)
    {
        var count = await db.Patients.CountAsync(ct);
        var n = count + 1;

        // Guard against gaps from deletions colliding with an existing number.
        while (true)
        {
            var fileNo = $"P-{n:D6}";
            var clash = await db.Patients.AnyAsync(p => p.FileNo == fileNo, ct);
            if (!clash) return fileNo;
            n++;
        }
    }

    public async Task<Patient> EnsureForUserAsync(string userId, CancellationToken ct = default)
    {
        var existing = await db.Patients.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (existing is not null) return existing;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("User not found");

        var parts = user.Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts.Length > 0 ? parts[0] : user.Email!.Split('@')[0];
        var lastName = parts.Length > 1 ? string.Join(' ', parts[1..]) : string.Empty;

        var patient = new Patient
        {
            FileNo = await NextFileNoAsync(ct),
            UserId = userId,
            FirstName = firstName,
            LastName = lastName,
            Email = user.Email,
            Phone = user.PhoneNumber,
        };

        db.Patients.Add(patient);
        await db.SaveChangesAsync(ct);
        return patient;
    }

    /// <summary>
    /// Guests still get a patient file. Matching on email keeps a returning guest
    /// on the same file rather than creating a new one each time.
    /// </summary>
    public async Task<Patient> FindOrCreateGuestAsync(
        string email, string? phone, string fullName, CancellationToken ct = default)
    {
        var normalised = email.ToLowerInvariant();

        var existing = await db.Patients
            .FirstOrDefaultAsync(p => p.Email == normalised && p.DeletedAt == null, ct);

        if (existing is not null) return existing;

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var patient = new Patient
        {
            FileNo = await NextFileNoAsync(ct),
            FirstName = parts.Length > 0 ? parts[0] : "Guest",
            LastName = parts.Length > 1 ? string.Join(' ', parts[1..]) : string.Empty,
            Email = normalised,
            Phone = phone,
        };

        db.Patients.Add(patient);
        await db.SaveChangesAsync(ct);
        return patient;
    }
}
