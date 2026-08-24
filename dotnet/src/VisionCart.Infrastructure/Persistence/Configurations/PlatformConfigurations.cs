using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VisionCart.Domain.Entities;

namespace VisionCart.Infrastructure.Persistence.Configurations;

public class TryOnSessionConfiguration : IEntityTypeConfiguration<TryOnSession>
{
    public void Configure(EntityTypeBuilder<TryOnSession> b)
    {
        b.ToTable("TryOnSession");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.UserId).HasMaxLength(Len.Id);
        b.Property(x => x.PatientId).HasMaxLength(Len.Id);
        b.Property(x => x.Source).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.PhotoUrl).HasMaxLength(Len.Url);
        b.Property(x => x.FaceData).HasColumnType("nvarchar(max)");

        b.HasIndex(x => x.UserId);
        b.HasIndex(x => x.PatientId);
        b.HasIndex(x => x.CreatedAt);

        b.HasOne(x => x.User)
            .WithMany(u => u.TryOnSessions)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.Patient)
            .WithMany(p => p.TryOnSessions)
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class TryOnSnapshotConfiguration : IEntityTypeConfiguration<TryOnSnapshot>
{
    public void Configure(EntityTypeBuilder<TryOnSnapshot> b)
    {
        b.ToTable("TryOnSnapshot");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.SessionId).HasMaxLength(Len.Id).IsRequired();
        b.Property(x => x.VariantId).HasMaxLength(Len.Id).IsRequired();
        b.Property(x => x.ImageUrl).HasMaxLength(Len.Url).IsRequired();

        b.HasIndex(x => x.SessionId);
        b.HasIndex(x => x.VariantId);

        b.HasOne(x => x.Session)
            .WithMany(s => s.Snapshots)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Second cascade path into TryOnSnapshot; demoted as elsewhere.
        b.HasOne(x => x.Variant)
            .WithMany(v => v.Snapshots)
            .HasForeignKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> b)
    {
        b.ToTable("Setting");
        // The key is the primary key, exactly as in the Prisma model.
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(Len.Code);
        b.Property(x => x.Group).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.Value).HasColumnType("nvarchar(max)");

        b.HasIndex(x => x.Group);
    }
}

public class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> b)
    {
        b.ToTable("MediaAsset");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.Url).HasMaxLength(Len.Url).IsRequired();
        b.Property(x => x.ThumbUrl).HasMaxLength(Len.Url);
        b.Property(x => x.Filename).HasMaxLength(Len.Name).IsRequired();
        b.Property(x => x.MimeType).HasMaxLength(128);
        b.Property(x => x.Tags).HasMaxLength(Len.CommaList);
        b.Property(x => x.UploadedBy).HasMaxLength(Len.Id);
        b.Property(x => x.StorageKey).HasMaxLength(Len.Url);
        b.Property(x => x.ThumbStorageKey).HasMaxLength(Len.Url);
        b.Property(x => x.StorageProvider).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.PurgeError).HasMaxLength(Len.ShortText);

        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => x.Filename);
        b.HasIndex(x => x.DeletedAt);

        // The retry sweep for cloud objects that failed to delete: rows marked
        // deleted but not yet purged. Without this the sweep is a table scan.
        b.HasIndex(x => new { x.DeletedAt, x.PurgedAt })
            .HasFilter("[DeletedAt] IS NOT NULL AND [PurgedAt] IS NULL");
    }
}

public class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> b)
    {
        b.ToTable("ImportJob");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.Kind).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.Filename).HasMaxLength(Len.Name).IsRequired();
        b.Property(x => x.Status).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.CreatedBy).HasMaxLength(Len.Id);

        // Row-level error reports can be long for a large bad file.
        b.Property(x => x.Report).HasColumnType("nvarchar(max)");

        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => new { x.Kind, x.Status });
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLog");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.UserId).HasMaxLength(Len.Id);
        b.Property(x => x.Action).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.Entity).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(Len.Id);
        b.Property(x => x.Ip).HasMaxLength(64);
        b.Property(x => x.ActorEmail).HasMaxLength(Len.Email);
        b.Property(x => x.UserAgent).HasMaxLength(Len.ShortText);
        b.Property(x => x.Detail).HasColumnType("nvarchar(max)");

        b.HasIndex(x => new { x.Entity, x.EntityId });
        b.HasIndex(x => x.CreatedAt);

        // Filters the new audit viewer offers.
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
        b.HasIndex(x => new { x.Action, x.CreatedAt });

        b.HasOne(x => x.User)
            .WithMany(u => u.AuditLogs)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class DataSubjectRequestConfiguration : IEntityTypeConfiguration<DataSubjectRequest>
{
    public void Configure(EntityTypeBuilder<DataSubjectRequest> b)
    {
        b.ToTable("DataSubjectRequest");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.UserId).HasMaxLength(Len.Id);
        b.Property(x => x.PatientId).HasMaxLength(Len.Id);
        b.Property(x => x.HandledByUserId).HasMaxLength(Len.Id);
        b.Property(x => x.Email).HasMaxLength(Len.Email).IsRequired();
        b.Property(x => x.Kind).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.Status).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.CustomerMessage).HasColumnType("nvarchar(max)");
        b.Property(x => x.StaffNotes).HasColumnType("nvarchar(max)");

        b.HasIndex(x => new { x.Status, x.CreatedAt });
        b.HasIndex(x => x.Email);

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.Patient)
            .WithMany()
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class OutboxEmailConfiguration : IEntityTypeConfiguration<OutboxEmail>
{
    public void Configure(EntityTypeBuilder<OutboxEmail> b)
    {
        b.ToTable("OutboxEmail");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.ToAddress).HasMaxLength(Len.Email).IsRequired();
        b.Property(x => x.ToName).HasMaxLength(Len.Name);
        b.Property(x => x.Subject).HasMaxLength(Len.ShortText).IsRequired();
        b.Property(x => x.Template).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.Status).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.LastError).HasMaxLength(Len.ShortText);
        b.Property(x => x.RelatedEntity).HasMaxLength(Len.Status);
        b.Property(x => x.RelatedEntityId).HasMaxLength(Len.Id);
        b.Property(x => x.HtmlBody).HasColumnType("nvarchar(max)");
        b.Property(x => x.TextBody).HasColumnType("nvarchar(max)");

        // The drain query: pending mail whose next attempt is due, oldest first.
        b.HasIndex(x => new { x.Status, x.NextAttemptAt });
        b.HasIndex(x => new { x.RelatedEntity, x.RelatedEntityId });
    }
}
