using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace VisionCart.Infrastructure.Persistence.Configurations;

/// <summary>
/// Identity's own tables need their key columns narrowed to match
/// <c>ApplicationUser.Id</c> and <c>ApplicationRole.Id</c>.
///
/// Identity defaults every key column to <c>nvarchar(450)</c> — the widest value
/// SQL Server will still index. VisionCart's keys are 25-character cuids, so the
/// parent columns were narrowed to 30. SQL Server requires both sides of a
/// foreign key to be declared with identical length and scale, so the child
/// tables have to be narrowed in step or the FKs are rejected outright
/// (error 1753). This also shrinks five indexes considerably.
///
/// LoginProvider and ProviderKey stay at 128: they hold values chosen by
/// external identity providers, not by this application.
/// </summary>
public class IdentityUserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<string>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<string>> b) =>
        b.Property(x => x.UserId).HasMaxLength(Len.Id);
}

public class IdentityUserRoleConfiguration : IEntityTypeConfiguration<IdentityUserRole<string>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<string>> b)
    {
        b.Property(x => x.UserId).HasMaxLength(Len.Id);
        b.Property(x => x.RoleId).HasMaxLength(Len.Id);
    }
}

public class IdentityUserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<string>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<string>> b)
    {
        b.Property(x => x.UserId).HasMaxLength(Len.Id);
        b.Property(x => x.LoginProvider).HasMaxLength(128);
        b.Property(x => x.ProviderKey).HasMaxLength(128);
    }
}

public class IdentityUserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<string>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<string>> b)
    {
        b.Property(x => x.UserId).HasMaxLength(Len.Id);
        b.Property(x => x.LoginProvider).HasMaxLength(128);
        b.Property(x => x.Name).HasMaxLength(128);
    }
}

public class IdentityRoleClaimConfiguration : IEntityTypeConfiguration<IdentityRoleClaim<string>>
{
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<string>> b) =>
        b.Property(x => x.RoleId).HasMaxLength(Len.Id);
}
