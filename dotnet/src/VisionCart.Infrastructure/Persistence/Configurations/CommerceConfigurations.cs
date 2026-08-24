using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VisionCart.Domain.Entities;

namespace VisionCart.Infrastructure.Persistence.Configurations;

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> b)
    {
        b.ToTable("Cart");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.Token).HasMaxLength(64).IsRequired();
        b.Property(x => x.UserId).HasMaxLength(Len.Id);
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.PromoCode).HasMaxLength(Len.Code);

        b.HasIndex(x => x.Token).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasIndex(x => x.UpdatedAt); // abandoned-cart sweep

        b.HasOne(x => x.User)
            .WithMany(u => u.Carts)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> b)
    {
        b.ToTable("CartItem");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.CartId).HasMaxLength(Len.Id).IsRequired();
        b.Property(x => x.VariantId).HasMaxLength(Len.Id).IsRequired();
        b.Property(x => x.PrescriptionId).HasMaxLength(Len.Id);
        b.Property(x => x.TryOnSnapshotId).HasMaxLength(Len.Id);
        b.Property(x => x.LensOptionCodes).HasMaxLength(Len.CommaList);
        b.Property(x => x.PrescriptionDraft).HasColumnType("nvarchar(max)");

        b.HasIndex(x => x.CartId);
        b.HasIndex(x => x.VariantId);

        b.HasOne(x => x.Cart)
            .WithMany(c => c.Items)
            .HasForeignKey(x => x.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        // MIGRATION NOTE — cascade demoted to NoAction.
        // Prisma cascaded a cart line away when its variant was deleted. Under
        // SQL Server that is a second cascade path into CartItem alongside Cart.
        // CatalogueService archives variants rather than deleting them, and
        // CartService drops lines whose variant has gone — behaviour the legacy
        // pricing layer already had, since priceLines() silently skipped a line
        // whose product no longer existed.
        b.HasOne(x => x.Variant)
            .WithMany(v => v.CartItems)
            .HasForeignKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("Order");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.OrderNo).HasMaxLength(32).IsRequired();
        b.Property(x => x.UserId).HasMaxLength(Len.Id);
        b.Property(x => x.PatientId).HasMaxLength(Len.Id);
        b.Property(x => x.Email).HasMaxLength(Len.Email).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(Len.Phone);
        b.Property(x => x.Status).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.PaymentStatus).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.FulfilmentStatus).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.PromoCode).HasMaxLength(Len.Code);
        b.Property(x => x.PromotionId).HasMaxLength(Len.Id);
        b.Property(x => x.ShippingAddressId).HasMaxLength(Len.Id);
        b.Property(x => x.BillingAddressId).HasMaxLength(Len.Id);
        b.Property(x => x.Notes).HasColumnType("nvarchar(max)");
        b.Property(x => x.InternalNotes).HasColumnType("nvarchar(max)");

        b.HasIndex(x => x.OrderNo).IsUnique();
        b.HasIndex(x => new { x.Status, x.PlacedAt });
        b.HasIndex(x => x.Email);
        b.HasIndex(x => x.PatientId);
        b.HasIndex(x => x.UserId);
        b.HasIndex(x => x.PaymentStatus);
        b.HasIndex(x => x.PlacedAt);          // dashboard "orders today"
        b.HasIndex(x => new { x.PromotionId, x.UserId }); // per-customer usage cap

        b.HasOne(x => x.User)
            .WithMany(u => u.Orders)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.Patient)
            .WithMany(p => p.Orders)
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.Promotion)
            .WithMany(p => p.Orders)
            .HasForeignKey(x => x.PromotionId)
            .OnDelete(DeleteBehavior.SetNull);

        // MIGRATION NOTE — two FKs to the same table.
        // SQL Server permits at most one cascading action along any path between
        // two tables, and Order reaches Address twice. Both are demoted to
        // NoAction; an address attached to an order is delivery evidence and is
        // soft-deleted from the customer's address book rather than removed.
        b.HasOne(x => x.ShippingAddress)
            .WithMany(a => a.ShippingOrders)
            .HasForeignKey(x => x.ShippingAddressId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.BillingAddress)
            .WithMany(a => a.BillingOrders)
            .HasForeignKey(x => x.BillingAddressId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("OrderItem");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.OrderId).HasMaxLength(Len.Id).IsRequired();
        b.Property(x => x.VariantId).HasMaxLength(Len.Id);
        b.Property(x => x.PrescriptionId).HasMaxLength(Len.Id);
        b.Property(x => x.TitleSnapshot).HasMaxLength(Len.Name).IsRequired();
        b.Property(x => x.SkuSnapshot).HasMaxLength(Len.Code).IsRequired();
        b.Property(x => x.ImageSnapshot).HasMaxLength(Len.Url);
        b.Property(x => x.LensOptionCodes).HasMaxLength(Len.CommaList);
        b.Property(x => x.LensSummary).HasMaxLength(Len.ShortText);
        b.Property(x => x.TryOnSnapshotUrl).HasMaxLength(Len.Url);
        b.Property(x => x.LabStatus).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.LabRef).HasMaxLength(Len.Code);

        // The frozen Rx must stand alone on an invoice years later.
        b.Property(x => x.PrescriptionSnapshot).HasColumnType("nvarchar(max)");

        b.HasIndex(x => x.OrderId);
        b.HasIndex(x => x.LabStatus);   // the lab queue
        b.HasIndex(x => x.VariantId);

        b.HasOne(x => x.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // MIGRATION NOTE — SetNull demoted to NoAction, and this is a strengthening.
        // Prisma allowed a variant or a prescription to be deleted out from under
        // a historical order line, nulling the reference. The frozen snapshot
        // columns meant the invoice still read correctly, but the link was lost.
        // Under SQL Server these paths would also collide with the Order cascade.
        // NoAction makes the database itself refuse to delete a prescription that
        // an order was dispensed against — the "prescriptions are immutable once
        // used" invariant is now enforced by the schema, not only by convention.
        // Restrict, not NoAction. Both emit the same restrictive foreign key, but
        // NoAction leaves EF Core's own client-side fixup in place: with the order
        // line tracked, EF would null PrescriptionId in memory and issue an UPDATE
        // before the DELETE, quietly severing the clinical link that the database
        // constraint exists to protect. Restrict disables that fixup, so the
        // attempt fails instead of succeeding by the back door.
        b.HasOne(x => x.Variant)
            .WithMany(v => v.OrderItems)
            .HasForeignKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Prescription)
            .WithMany(p => p.OrderItems)
            .HasForeignKey(x => x.PrescriptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("Payment");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.OrderId).HasMaxLength(Len.Id).IsRequired();
        b.Property(x => x.Provider).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.Status).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.ProviderRef).HasMaxLength(Len.Code);
        b.Property(x => x.IdempotencyKey).HasMaxLength(Len.Code);
        b.Property(x => x.Error).HasMaxLength(Len.ShortText);

        // Provider payloads are large and are kept verbatim for reconciliation.
        b.Property(x => x.RawPayload).HasColumnType("nvarchar(max)");

        b.HasIndex(x => x.OrderId);
        b.HasIndex(x => x.ProviderRef);
        b.HasIndex(x => x.Status);

        // Replay protection for the payment webhook. A provider redelivering an
        // event cannot mark the same order paid twice: the second insert violates
        // this constraint and is handled as an already-processed no-op.
        b.HasIndex(x => x.IdempotencyKey)
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL");

        b.HasOne(x => x.Order)
            .WithMany(o => o.Payments)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> b)
    {
        b.ToTable("Shipment");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.OrderId).HasMaxLength(Len.Id).IsRequired();
        b.Property(x => x.Carrier).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.Service).HasMaxLength(Len.Name);
        b.Property(x => x.TrackingNumber).HasMaxLength(Len.Code);
        b.Property(x => x.TrackingUrl).HasMaxLength(Len.Url);
        b.Property(x => x.LabelUrl).HasMaxLength(Len.Url);
        b.Property(x => x.Status).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.ProviderRef).HasMaxLength(Len.Code);

        b.HasIndex(x => x.OrderId);
        b.HasIndex(x => x.TrackingNumber);

        b.HasOne(x => x.Order)
            .WithMany(o => o.Shipments)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ShippingRateConfiguration : IEntityTypeConfiguration<ShippingRate>
{
    public void Configure(EntityTypeBuilder<ShippingRate> b)
    {
        b.ToTable("ShippingRate");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.Name).HasMaxLength(Len.Name).IsRequired();
        b.Property(x => x.Code).HasMaxLength(Len.Code);
        b.Property(x => x.Country).HasMaxLength(2).IsRequired();
        b.Property(x => x.Region).HasMaxLength(Len.Name);
        b.Property(x => x.Carrier).HasMaxLength(Len.Status);

        b.HasIndex(x => new { x.IsActive, x.Country, x.Position });
        b.HasIndex(x => new { x.EffectiveFrom, x.EffectiveTo });
    }
}

public class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> b)
    {
        b.ToTable("Promotion");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(Len.Id);
        b.Property(x => x.Name).HasMaxLength(Len.Name).IsRequired();
        b.Property(x => x.Code).HasMaxLength(Len.Code);
        b.Property(x => x.Kind).HasMaxLength(Len.Status).IsRequired();
        b.Property(x => x.BrandIds).HasMaxLength(Len.CommaList);
        b.Property(x => x.CategoryIds).HasMaxLength(Len.CommaList);
        b.Property(x => x.FrameIds).HasMaxLength(Len.CommaList);
        b.Property(x => x.BannerText).HasMaxLength(Len.ShortText);
        b.Property(x => x.BannerColor).HasMaxLength(16);
        b.Property(x => x.Description).HasColumnType("nvarchar(max)");

        // Prisma's @unique on a nullable column allowed many NULLs. SQL Server's
        // plain unique index allows only one, so the filter reproduces the
        // original behaviour: many automatic promotions, unique codes.
        b.HasIndex(x => x.Code).IsUnique().HasFilter("[Code] IS NOT NULL");

        b.HasIndex(x => new { x.IsActive, x.StartsAt, x.EndsAt });
        b.HasIndex(x => x.Priority);
    }
}
