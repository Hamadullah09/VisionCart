using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VisionCart.Domain.Entities;

namespace VisionCart.Infrastructure.Persistence.Configurations;

public class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> b)
    {
        b.ToTable("Brand");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.Name).HasMaxLength(Len.Name).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(Len.Code).IsRequired();
        b.Property(x => x.LogoUrl).HasMaxLength(Len.Url);
        b.Property(x => x.About).HasColumnType("nvarchar(max)");

        b.HasIndex(x => x.Name).IsUnique();
        b.HasIndex(x => x.Slug).IsUnique();
    }
}

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("Category");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.Name).HasMaxLength(Len.Name).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(Len.Code).IsRequired();
        b.Property(x => x.ParentId).HasMaxLength(Len.Id);

        b.HasIndex(x => x.Slug).IsUnique();
        b.HasIndex(x => x.ParentId);

        // MIGRATION NOTE — SetNull demoted to NoAction.
        // SQL Server refuses any cascading action on a self-referencing foreign
        // key, because the engine cannot prove the graph is acyclic. Re-parenting
        // children is done explicitly in CategoryService before a delete.
        b.HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public class FrameCategoryConfiguration : IEntityTypeConfiguration<FrameCategory>
{
    public void Configure(EntityTypeBuilder<FrameCategory> b)
    {
        b.ToTable("FrameCategory");
        b.HasKey(x => new { x.FrameId, x.CategoryId });
        b.Property(x => x.FrameId).HasMaxLength(Len.Id);
        b.Property(x => x.CategoryId).HasMaxLength(Len.Id);

        b.HasIndex(x => x.CategoryId);

        b.HasOne(x => x.Frame)
            .WithMany(f => f.Categories)
            .HasForeignKey(x => x.FrameId)
            .OnDelete(DeleteBehavior.Cascade);

        // Only one side may cascade into a join table under SQL Server.
        // Removing a category detaches its frames through CategoryService.
        b.HasOne(x => x.Category)
            .WithMany(c => c.Frames)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public class FrameConfiguration : IEntityTypeConfiguration<Frame>
{
    public void Configure(EntityTypeBuilder<Frame> b)
    {
        b.ToTable("Frame");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.Sku).HasMaxLength(Len.Code).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(Len.Code).IsRequired();
        b.Property(x => x.Name).HasMaxLength(Len.Name).IsRequired();
        b.Property(x => x.BrandId).HasMaxLength(Len.Id);
        b.Property(x => x.Shape).HasMaxLength(Len.Status);
        b.Property(x => x.Material).HasMaxLength(Len.Status);
        b.Property(x => x.RimType).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.Gender).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.FaceShapes).HasMaxLength(Len.CommaList);
        b.Property(x => x.SizeBand).HasMaxLength(Len.Status);
        b.Property(x => x.Status).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.MetaTitle).HasMaxLength(Len.ShortText);
        b.Property(x => x.MetaDesc).HasMaxLength(Len.ShortText);
        b.Property(x => x.Description).HasColumnType("nvarchar(max)");
        b.Property(x => x.SearchText).HasMaxLength(1024);

        b.HasIndex(x => x.Sku).IsUnique();
        b.HasIndex(x => x.Slug).IsUnique();
        b.HasIndex(x => new { x.Status, x.IsFeatured });
        b.HasIndex(x => x.Shape);
        b.HasIndex(x => x.BrandId);

        // Filter combinations the catalogue page actually issues.
        b.HasIndex(x => new { x.Status, x.Gender, x.Shape });
        b.HasIndex(x => new { x.Status, x.BasePriceMinor });
        b.HasIndex(x => x.SearchText);

        b.HasOne(x => x.Brand)
            .WithMany(x => x.Frames)
            .HasForeignKey(x => x.BrandId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class FrameVariantConfiguration : IEntityTypeConfiguration<FrameVariant>
{
    public void Configure(EntityTypeBuilder<FrameVariant> b)
    {
        b.ToTable("FrameVariant");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.FrameId).HasMaxLength(Len.Id).IsRequired();
        b.Property(x => x.Sku).HasMaxLength(Len.Code).IsRequired();
        b.Property(x => x.ColorName).HasMaxLength(Len.Name).IsRequired();
        b.Property(x => x.ColorHex).HasMaxLength(16);
        b.Property(x => x.Barcode).HasMaxLength(Len.Code);
        b.Property(x => x.TryOnImageUrl).HasMaxLength(Len.Url);

        b.Ignore(x => x.IsTryOnReady);

        b.HasIndex(x => x.Sku).IsUnique();
        b.HasIndex(x => new { x.FrameId, x.IsActive });
        b.HasIndex(x => x.Barcode);
        b.HasIndex(x => x.StockQty);

        b.HasOne(x => x.Frame)
            .WithMany(f => f.Variants)
            .HasForeignKey(x => x.FrameId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> b)
    {
        b.ToTable("ProductImage");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.VariantId).HasMaxLength(Len.Id).IsRequired();
        b.Property(x => x.Url).HasMaxLength(Len.Url).IsRequired();
        b.Property(x => x.ThumbUrl).HasMaxLength(Len.Url);
        b.Property(x => x.Alt).HasMaxLength(Len.ShortText);
        b.Property(x => x.Role).HasMaxLength(Len.Status).IsRequired();

        b.HasIndex(x => new { x.VariantId, x.Position });

        b.HasOne(x => x.Variant)
            .WithMany(v => v.Images)
            .HasForeignKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class LensOptionConfiguration : IEntityTypeConfiguration<LensOption>
{
    public void Configure(EntityTypeBuilder<LensOption> b)
    {
        b.ToTable("LensOption");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.Group).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.Code).HasMaxLength(Len.Code).IsRequired();
        b.Property(x => x.Name).HasMaxLength(Len.Name).IsRequired();
        b.Property(x => x.Description).HasMaxLength(Len.ShortText);
        b.Property(x => x.Requires).HasMaxLength(Len.CommaList);
        b.Property(x => x.Excludes).HasMaxLength(Len.CommaList);

        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => new { x.Group, x.Position });
        b.HasIndex(x => x.IsActive);
    }
}
