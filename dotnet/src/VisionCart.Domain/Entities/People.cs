using Microsoft.AspNetCore.Identity;
using VisionCart.Domain.Constants;
using VisionCart.Domain.ValueObjects;

namespace VisionCart.Domain.Entities;

/// <summary>
/// The application user. Derives from <see cref="IdentityUser{TKey}"/> with a
/// string key so the existing cuid identifiers survive the migration untouched.
///
/// Identity supplies what the legacy implementation hand-rolled: PBKDF2 password
/// hashing (replacing bcryptjs), lockout counters (which the legacy system had no
/// equivalent of), and single-use expiring tokens — the mechanism the new password
/// reset flow is built on.
///
/// <see cref="Role"/> is retained alongside Identity's own role tables. Identity
/// roles are authoritative for authorization; this column mirrors the user's single
/// primary role so CSV import/export and audit records keep the legacy shape.
/// </summary>
public class ApplicationUser : IdentityUser<string>
{
    public ApplicationUser()
    {
        Id = Cuid.NewId();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string Name { get; set; } = string.Empty;

    /// <summary>customer | staff | optician | admin</summary>
    public string Role { get; set; } = Roles.Customer;

    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Patient? Patient { get; set; }
    public ICollection<Address> Addresses { get; set; } = [];
    public ICollection<Order> Orders { get; set; } = [];
    public ICollection<Cart> Carts { get; set; } = [];
    public ICollection<TryOnSession> TryOnSessions { get; set; } = [];
    public ICollection<AuditLog> AuditLogs { get; set; } = [];
}

public class ApplicationRole : IdentityRole<string>
{
    public ApplicationRole() => Id = Cuid.NewId();
    public ApplicationRole(string name) : this() { Name = name; NormalizedName = name.ToUpperInvariant(); }
}

/// <summary>
/// Clinical + commercial record kept for the lifetime of the customer
/// relationship. One per person; prescriptions are versioned children so
/// historical Rx used on past orders is never overwritten.
/// </summary>
public class Patient
{
    public string Id { get; set; } = Cuid.NewId();

    /// <summary>Human-friendly file number shown in the back office, e.g. P-000123.</summary>
    public string FileNo { get; set; } = string.Empty;

    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }

    /// <summary>Free-text clinical context: allergies, prior surgery, dry-eye, etc.</summary>
    public string? Notes { get; set; }

    /// <summary>Measured or supplied interpupillary distance in mm (binocular).</summary>
    public double? PdMm { get; set; }
    public double? PdNearMm { get; set; }

    /// <summary>Face measurements captured by the try-on tool, JSON string.</summary>
    public string? FaceMetrics { get; set; }
    public string? Tags { get; set; }

    // Consent & retention (kept explicit — this is health data)
    public bool ConsentMarketing { get; set; }
    public DateTime? ConsentDataAt { get; set; }
    public string? ConsentVersion { get; set; }
    public DateTime? RetentionUntil { get; set; }
    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Prescription> Prescriptions { get; set; } = [];
    public ICollection<Order> Orders { get; set; } = [];
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<PatientDocument> Documents { get; set; } = [];
    public ICollection<TryOnSession> TryOnSessions { get; set; } = [];
}

/// <summary>
/// One prescription version. Immutable once used on an order — the order line
/// stores its own snapshot as well, so re-issuing an Rx never rewrites history.
/// </summary>
public class Prescription
{
    public string Id { get; set; } = Cuid.NewId();
    public string PatientId { get; set; } = string.Empty;
    public Patient Patient { get; set; } = null!;

    /// <summary>uploaded | in_store_exam | manual_entry | imported</summary>
    public string Source { get; set; } = RxSources.ManualEntry;

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public string? Prescriber { get; set; }
    public string? Clinic { get; set; }

    /// <summary>Scan/photo of the paper Rx.</summary>
    public string? DocumentUrl { get; set; }
    public string? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }

    /// <summary>draft | pending_verification | verified | rejected | expired</summary>
    public string Status { get; set; } = RxStatuses.Draft;

    // Right eye (OD)
    public double? OdSphere { get; set; }
    public double? OdCylinder { get; set; }
    public int? OdAxis { get; set; }
    public double? OdAdd { get; set; }
    public double? OdPrism { get; set; }
    public string? OdPrismBase { get; set; }
    public double? OdPdMm { get; set; }

    // Left eye (OS)
    public double? OsSphere { get; set; }
    public double? OsCylinder { get; set; }
    public int? OsAxis { get; set; }
    public double? OsAdd { get; set; }
    public double? OsPrism { get; set; }
    public string? OsPrismBase { get; set; }
    public double? OsPdMm { get; set; }

    // Progressive / bifocal fitting heights
    public double? OdSegHeightMm { get; set; }
    public double? OsSegHeightMm { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<OrderItem> OrderItems { get; set; } = [];
}

public class PatientDocument
{
    public string Id { get; set; } = Cuid.NewId();
    public string PatientId { get; set; } = string.Empty;
    public Patient Patient { get; set; } = null!;

    /// <summary>prescription_scan | id_document | insurance | photo | other</summary>
    public string Kind { get; set; } = PatientDocumentKinds.Other;

    public string? Label { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? MimeType { get; set; }
    public int? SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Appointment
{
    public string Id { get; set; } = Cuid.NewId();
    public string PatientId { get; set; } = string.Empty;
    public Patient Patient { get; set; } = null!;

    public DateTime StartsAt { get; set; }
    public int Minutes { get; set; } = 30;

    /// <summary>eye_test | fitting | collection | adjustment | follow_up</summary>
    public string Kind { get; set; } = AppointmentKinds.EyeTest;

    /// <summary>scheduled | completed | no_show | cancelled</summary>
    public string Status { get; set; } = AppointmentStatuses.Scheduled;

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // --- Added during migration -------------------------------------------
    // The legacy table stored only the patient side of the booking, which is
    // not enough to run a clinic diary: reception needs to know which optician
    // the slot belongs to, and reminders need to know whether one was sent.

    /// <summary>Staff/optician the appointment is booked with.</summary>
    public string? StaffUserId { get; set; }
    public ApplicationUser? StaffUser { get; set; }

    public DateTime? ReminderSentAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledReason { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Computed end of the slot; used for overlap checks.</summary>
    public DateTime EndsAt => StartsAt.AddMinutes(Minutes);
}

public class Address
{
    public string Id { get; set; } = Cuid.NewId();
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public string? Label { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string Country { get; set; } = "PK";
    public bool IsDefault { get; set; }

    /// <summary>
    /// Added during migration. Orders reference the address row they shipped to,
    /// so a customer removing an address from their address book must not erase
    /// the delivery record of a past order — it is hidden from the book instead.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    public ICollection<Order> ShippingOrders { get; set; } = [];
    public ICollection<Order> BillingOrders { get; set; } = [];
}
