using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VisionCart.Domain.Entities;

namespace VisionCart.Infrastructure.Persistence.Configurations;

/// <summary>
/// Shared column widths. Prisma stored everything as unbounded TEXT; SQL Server
/// needs real lengths so indexes and unique constraints are possible.
/// </summary>
internal static class Len
{
    public const int Id = 30;          // cuid() is 25 chars; 30 leaves headroom
    public const int Code = 128;       // slug, sku, barcode, promo code
    public const int Name = 200;
    public const int Email = 256;
    public const int Phone = 32;
    public const int Status = 40;      // status / kind / role / group discriminators
    public const int Url = 1024;
    public const int ShortText = 512;
    public const int CommaList = 2048; // comma-separated id/code lists
}

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.Name).HasMaxLength(Len.Name).IsRequired();
        b.Property(x => x.Role).HasMaxLength(Len.Status).IsRequired();
        b.HasIndex(x => x.Role);
        b.HasIndex(x => x.IsActive);
    }
}

public class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> b) =>
        b.Property(x => x.Id).HasMaxLength(Len.Id);
}

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> b)
    {
        b.ToTable("Patient");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.FileNo).HasMaxLength(20).IsRequired();
        b.Property(x => x.UserId).HasMaxLength(Len.Id);
        b.Property(x => x.FirstName).HasMaxLength(Len.Name).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(Len.Name).IsRequired();
        b.Property(x => x.Email).HasMaxLength(Len.Email);
        b.Property(x => x.Phone).HasMaxLength(Len.Phone);
        b.Property(x => x.Gender).HasMaxLength(Len.Status);
        b.Property(x => x.ConsentVersion).HasMaxLength(Len.Status);
        b.Property(x => x.Tags).HasMaxLength(Len.CommaList);

        // Clinical free text and captured metrics can be long.
        b.Property(x => x.Notes).HasColumnType("nvarchar(max)");
        b.Property(x => x.FaceMetrics).HasColumnType("nvarchar(max)");

        b.HasIndex(x => x.FileNo).IsUnique();
        b.HasIndex(x => new { x.LastName, x.FirstName });
        b.HasIndex(x => x.Phone);
        b.HasIndex(x => x.Email);
        b.HasIndex(x => x.DeletedAt);

        b.HasOne(x => x.User)
            .WithOne(u => u.Patient)
            .HasForeignKey<Patient>(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // One patient file per user account.
        b.HasIndex(x => x.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");
    }
}

public class PrescriptionConfiguration : IEntityTypeConfiguration<Prescription>
{
    public void Configure(EntityTypeBuilder<Prescription> b)
    {
        b.ToTable("Prescription");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.PatientId).HasMaxLength(Len.Id).IsRequired();
        b.Property(x => x.Source).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.Status).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.Prescriber).HasMaxLength(Len.Name);
        b.Property(x => x.Clinic).HasMaxLength(Len.Name);
        b.Property(x => x.DocumentUrl).HasMaxLength(Len.Url);
        b.Property(x => x.VerifiedBy).HasMaxLength(Len.Id);
        b.Property(x => x.OdPrismBase).HasMaxLength(8);
        b.Property(x => x.OsPrismBase).HasMaxLength(8);
        b.Property(x => x.Notes).HasColumnType("nvarchar(max)");

        b.HasIndex(x => new { x.PatientId, x.IssuedAt });
        b.HasIndex(x => x.Status);

        b.HasOne(x => x.Patient)
            .WithMany(p => p.Prescriptions)
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PatientDocumentConfiguration : IEntityTypeConfiguration<PatientDocument>
{
    public void Configure(EntityTypeBuilder<PatientDocument> b)
    {
        b.ToTable("PatientDocument");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.PatientId).HasMaxLength(Len.Id).IsRequired();
        b.Property(x => x.Kind).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.Label).HasMaxLength(Len.Name);
        b.Property(x => x.Url).HasMaxLength(Len.Url).IsRequired();
        b.Property(x => x.MimeType).HasMaxLength(128);

        b.HasIndex(x => x.PatientId);

        b.HasOne(x => x.Patient)
            .WithMany(p => p.Documents)
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> b)
    {
        b.ToTable("Appointment");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.PatientId).HasMaxLength(Len.Id).IsRequired();
        b.Property(x => x.StaffUserId).HasMaxLength(Len.Id);
        b.Property(x => x.Kind).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.Status).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.CancelledReason).HasMaxLength(Len.ShortText);
        b.Property(x => x.Notes).HasColumnType("nvarchar(max)");

        // EndsAt is derived; it must not become a column.
        b.Ignore(x => x.EndsAt);

        b.HasIndex(x => x.StartsAt);
        b.HasIndex(x => new { x.StaffUserId, x.StartsAt });
        b.HasIndex(x => new { x.PatientId, x.StartsAt });
        b.HasIndex(x => x.Status);

        b.HasOne(x => x.Patient)
            .WithMany(p => p.Appointments)
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.StaffUser)
            .WithMany()
            .HasForeignKey(x => x.StaffUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> b)
    {
        b.ToTable("Address");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.UserId).HasMaxLength(Len.Id);
        b.Property(x => x.Label).HasMaxLength(Len.Name);
        b.Property(x => x.FullName).HasMaxLength(Len.Name).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(Len.Phone);
        b.Property(x => x.Line1).HasMaxLength(Len.ShortText).IsRequired();
        b.Property(x => x.Line2).HasMaxLength(Len.ShortText);
        b.Property(x => x.City).HasMaxLength(Len.Name).IsRequired();
        b.Property(x => x.State).HasMaxLength(Len.Name);
        b.Property(x => x.PostalCode).HasMaxLength(32);
        b.Property(x => x.Country).HasMaxLength(2).IsRequired();

        b.HasIndex(x => x.UserId);
        b.HasIndex(x => new { x.UserId, x.IsDefault });

        // MIGRATION NOTE — cascade demoted to NoAction.
        // Prisma cascaded Address from User. Under SQL Server that would create
        // three cascade paths into Order (User -> Order, and User -> Address ->
        // Order twice, once for the shipping FK and once for the billing FK),
        // which the engine rejects outright. Address rows referenced by an order
        // are permanent delivery evidence and must survive the account anyway, so
        // account closure anonymises addresses in AccountService instead of
        // deleting them, and the address book uses the DeletedAt soft delete.
        b.HasOne(x => x.User)
            .WithMany(u => u.Addresses)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
