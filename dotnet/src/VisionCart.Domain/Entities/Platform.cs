using VisionCart.Domain.Constants;
using VisionCart.Domain.ValueObjects;

namespace VisionCart.Domain.Entities;

public class TryOnSession
{
    public string Id { get; set; } = Cuid.NewId();
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public string? PatientId { get; set; }
    public Patient? Patient { get; set; }

    /// <summary>upload | camera</summary>
    public string Source { get; set; } = TryOnSources.Upload;

    public string? PhotoUrl { get; set; }

    /// <summary>Detected landmark summary + estimated PD, JSON string.</summary>
    public string? FaceData { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TryOnSnapshot> Snapshots { get; set; } = [];
}

public class TryOnSnapshot
{
    public string Id { get; set; } = Cuid.NewId();
    public string SessionId { get; set; } = string.Empty;
    public TryOnSession Session { get; set; } = null!;
    public string VariantId { get; set; } = string.Empty;
    public FrameVariant Variant { get; set; } = null!;
    public string ImageUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Key/value store config editable from the admin UI, so the shop can be
/// re-branded and re-priced without touching config files or redeploying.
/// </summary>
public class Setting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Group { get; set; } = "general";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class MediaAsset
{
    public string Id { get; set; } = Cuid.NewId();
    public string Url { get; set; } = string.Empty;
    public string? ThumbUrl { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string? MimeType { get; set; }
    public int? SizeBytes { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    /// <summary>Free tagging so the bulk uploader can group a shoot.</summary>
    public string? Tags { get; set; }

    public string? UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Added during migration. The legacy cloud driver deleted the database row
    /// but deliberately left the object in the bucket, so cloud storage grew
    /// without bound. Deletion now records the storage key and the outcome, and
    /// anything that failed to delete stays visible for a retry sweep instead of
    /// silently becoming an orphan.
    /// </summary>
    public string? StorageKey { get; set; }

    public string? ThumbStorageKey { get; set; }
    public string StorageProvider { get; set; } = "local";
    public DateTime? DeletedAt { get; set; }
    public DateTime? PurgedAt { get; set; }
    public string? PurgeError { get; set; }
    public int PurgeAttempts { get; set; }
}

/// <summary>Record of every CSV/bulk import so a bad file can be traced and rolled back.</summary>
public class ImportJob
{
    public string Id { get; set; } = Cuid.NewId();
    public string Kind { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;

    /// <summary>pending | running | completed | failed</summary>
    public string Status { get; set; } = ImportJobStatuses.Pending;

    public int TotalRows { get; set; }
    public int OkRows { get; set; }
    public int ErrorRows { get; set; }

    /// <summary>Row-level errors, JSON string.</summary>
    public string? Report { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    /// <summary>Added during migration: distinguishes a dry run from a real import.</summary>
    public bool IsDryRun { get; set; }
}

public class AuditLog
{
    public string Id { get; set; } = Cuid.NewId();
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public string Action { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public string? EntityId { get; set; }

    /// <summary>
    /// Before/after diff, JSON string. Clinical values are deliberately kept out
    /// of here — the log is read far more widely than the record it describes.
    /// </summary>
    public string? Detail { get; set; }

    public string? Ip { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Added during migration so the new audit viewer can attribute an entry
    /// without a join, and still read correctly after a staff account is deleted.
    /// </summary>
    public string? ActorEmail { get; set; }

    public string? UserAgent { get; set; }
}

/// <summary>
/// Added during migration. Serves the outstanding privacy obligation the legacy
/// system had fields for but no workflow: customers can ask for their record to be
/// corrected or erased, and staff must be able to see, action and evidence that.
/// </summary>
public class DataSubjectRequest
{
    public string Id { get; set; } = Cuid.NewId();

    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public string? PatientId { get; set; }
    public Patient? Patient { get; set; }

    /// <summary>Contact address for a request raised by someone without an account.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>correction | erasure | export | restriction</summary>
    public string Kind { get; set; } = "correction";

    /// <summary>pending | in_review | completed | rejected</summary>
    public string Status { get; set; } = "pending";

    public string? CustomerMessage { get; set; }
    public string? StaffNotes { get; set; }
    public string? HandledByUserId { get; set; }
    public DateTime? HandledAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Added during migration. Outbound mail is queued rather than sent inline so a
/// slow or unavailable SMTP server can never stall checkout, and so a failed
/// order confirmation is retried rather than lost. Drained by a hosted service
/// inside the same worker process — no external worker is required, which keeps
/// the deployment inside what shared IIS hosting supports.
/// </summary>
public class OutboxEmail
{
    public string Id { get; set; } = Cuid.NewId();
    public string ToAddress { get; set; } = string.Empty;
    public string? ToName { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public string? TextBody { get; set; }

    /// <summary>Template identifier, for diagnostics and resend.</summary>
    public string Template { get; set; } = string.Empty;

    /// <summary>pending | sent | failed | abandoned</summary>
    public string Status { get; set; } = "pending";

    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Related entity, so a resend can be traced back to its order.</summary>
    public string? RelatedEntity { get; set; }
    public string? RelatedEntityId { get; set; }
}
