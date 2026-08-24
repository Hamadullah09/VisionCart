using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VisionCart.Application.Carts;
using VisionCart.Application.Checkout;
using VisionCart.Application.Prescriptions;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;
using VisionCart.Infrastructure.Persistence;

namespace VisionCart.IntegrationTests;

/// <summary>
/// End-to-end verification of the customer purchase path, driven through the
/// real services against real SQL Server.
///
/// A green redirect proves nothing on its own. What matters is what the order
/// left behind: stock reserved, a patient file created for a guest, the typed
/// prescription promoted to a versioned record awaiting an optician, the frozen
/// snapshot written, the promotion counted, the bag consumed, and the audit
/// trail recorded without clinical values in it.
/// </summary>
[Collection("checkout")]
public class CheckoutSideEffectTests(CheckoutFlowFixture fixture)
{
    private static CheckoutInput ValidCheckout(string email) => new()
    {
        Email = email,
        Phone = "+92 300 1112222",
        FullName = "Integration Test Buyer",
        Line1 = "1 Test Street",
        City = "Lahore",
        State = "Punjab",
        PostalCode = "54000",
        Country = "PK",
        PaymentMethod = PaymentProviders.Cod,
    };

    /// <summary>Adds a frame to a fresh bag and places an order. Returns the order id.</summary>
    private async Task<string> PlaceOrderAsync(
        string email, string? promoCode = null, PrescriptionInput? rx = null)
    {
        fixture.ResetCart();

        var variantId = await fixture.SellableVariantIdAsync();

        using (var scope = fixture.NewScope())
        {
            var carts = scope.ServiceProvider.GetRequiredService<ICartService>();
            var added = await carts.AddAsync(new AddToCartRequest
            {
                VariantId = variantId,
                Qty = 1,
                LensOptionCodes = rx is null ? [] : ["type-single", "idx-150", "coat-hard"],
                PrescriptionDraft = rx,
            });
            Assert.True(added.Ok, added.Error);

            if (promoCode is not null)
            {
                var applied = await carts.ApplyPromoAsync(promoCode);
                Assert.True(applied.Ok, applied.Error);
            }
        }

        using (var scope = fixture.NewScope())
        {
            var checkout = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
            var outcome = await checkout.PlaceOrderAsync(ValidCheckout(email));
            Assert.True(outcome.Ok, outcome.Error);
            Assert.NotNull(outcome.OrderNo);

            using var read = fixture.NewScope();
            var db = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.Orders.Where(o => o.OrderNo == outcome.OrderNo).Select(o => o.Id).FirstAsync();
        }
    }

    private async Task<Order> LoadAsync(string orderId)
    {
        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Include(o => o.Shipments)
            .Include(o => o.ShippingAddress)
            .FirstAsync(o => o.Id == orderId);
    }

    [Fact]
    public async Task A_guest_can_buy_a_frame_end_to_end()
    {
        var orderId = await PlaceOrderAsync($"guest-{Guid.NewGuid():N}@example.com");
        var order = await LoadAsync(orderId);

        // VC-2026-000001 — the format a customer reads out over the phone.
        Assert.Matches(@"^VC-\d{4}-\d{6}$", order.OrderNo);
        Assert.Equal(OrderStatuses.Pending, order.Status);
        Assert.Equal(PaymentStatuses.Unpaid, order.PaymentStatus);
        Assert.Single(order.Items);
        Assert.NotNull(order.ShippingAddress);
    }

    [Fact]
    public async Task Totals_are_internally_consistent_in_integer_minor_units()
    {
        var orderId = await PlaceOrderAsync($"totals-{Guid.NewGuid():N}@example.com");
        var order = await LoadAsync(orderId);

        var expected = order.SubtotalMinor + order.LensTotalMinor
                       - order.DiscountMinor + order.ShippingMinor + order.TaxMinor;

        Assert.Equal(expected, order.TotalMinor);
        Assert.Equal(order.SubtotalMinor + order.LensTotalMinor, order.Items.Sum(i => i.TotalMinor));
        Assert.True(order.TotalMinor > 0);
    }

    [Fact]
    public async Task Order_lines_carry_their_own_frozen_snapshot()
    {
        var orderId = await PlaceOrderAsync($"snapshot-{Guid.NewGuid():N}@example.com");
        var order = await LoadAsync(orderId);

        foreach (var item in order.Items)
        {
            // These must stand alone on an invoice years later, even if the
            // catalogue row is renamed, re-priced or archived.
            Assert.False(string.IsNullOrWhiteSpace(item.TitleSnapshot));
            Assert.False(string.IsNullOrWhiteSpace(item.SkuSnapshot));
            Assert.True(item.UnitPriceMinor > 0);
            Assert.Equal(LabStatuses.Pending, item.LabStatus);
        }
    }

    [Fact]
    public async Task Every_order_gets_a_patient_file_even_for_a_guest()
    {
        var email = $"patient-{Guid.NewGuid():N}@example.com";
        var orderId = await PlaceOrderAsync(email);
        var order = await LoadAsync(orderId);

        // An optical order without a patient file cannot be remade or followed up.
        Assert.NotNull(order.PatientId);

        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var patient = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == order.PatientId);

        Assert.Matches(@"^P-\d{6}$", patient.FileNo);
        Assert.Equal(email, patient.Email);
    }

    [Fact]
    public async Task A_returning_guest_keeps_the_same_patient_file()
    {
        // Matching on email is what stops a repeat guest accumulating a new
        // clinical record on every order.
        var email = $"returning-{Guid.NewGuid():N}@example.com";

        var first = await LoadAsync(await PlaceOrderAsync(email));
        var second = await LoadAsync(await PlaceOrderAsync(email));

        Assert.Equal(first.PatientId, second.PatientId);
    }

    [Fact]
    public async Task Stock_is_reserved_at_order_time()
    {
        fixture.ResetCart();

        var variantId = await fixture.SellableVariantIdAsync();

        int before;
        using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Read the count *after* the top-up, so the delta below is measured
            // against the shelf the order will actually draw from.
            before = await db.FrameVariants.AsNoTracking()
                .Where(v => v.Id == variantId).Select(v => v.StockQty).FirstAsync();
        }

        await PlaceOrderAsync($"stock-{Guid.NewGuid():N}@example.com");

        using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var after = await db.FrameVariants.AsNoTracking()
                .Where(v => v.Id == variantId).Select(v => v.StockQty).FirstAsync();

            // Selling the last frame twice is far more expensive to unpick than
            // a brief oversell window is to avoid.
            Assert.Equal(before - 1, after);
            Assert.True(after >= 0, "stock must never go negative");
        }
    }

    [Fact]
    public async Task A_prescription_typed_at_checkout_becomes_a_versioned_record_awaiting_an_optician()
    {
        var rx = new PrescriptionInput
        {
            Od = new EyeRx { Sphere = -2.25, Cylinder = -0.75, Axis = 175 },
            Os = new EyeRx { Sphere = -2.50, Cylinder = -0.50, Axis = 10 },
            PdMm = 62.5,
        };

        var orderId = await PlaceOrderAsync($"rx-{Guid.NewGuid():N}@example.com", rx: rx);
        var order = await LoadAsync(orderId);

        var line = order.Items.First();
        Assert.NotNull(line.PrescriptionId);

        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.Prescriptions.AsNoTracking().FirstAsync(p => p.Id == line.PrescriptionId);

        // Never trusted straight into the lab: an optician verifies it first.
        Assert.Equal(RxStatuses.PendingVerification, saved.Status);
        Assert.Equal(RxSources.ManualEntry, saved.Source);
        Assert.Equal(-2.25, saved.OdSphere);
        Assert.Equal(175, saved.OdAxis);

        // The line kept its own copy, independent of the patient's file.
        Assert.NotNull(line.PrescriptionSnapshot);
        Assert.Contains("summary", line.PrescriptionSnapshot!);

        // And the binocular PD went onto the person, not the prescription.
        var patient = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == order.PatientId);
        Assert.Equal(62.5, patient.PdMm);
    }

    [Fact]
    public async Task An_unfillable_prescription_is_refused_before_it_reaches_a_lab_ticket()
    {
        fixture.ResetCart();

        var variantId = await fixture.SellableVariantIdAsync(minStock: 1);

        using var scope = fixture.NewScope();

        var carts = scope.ServiceProvider.GetRequiredService<ICartService>();

        // -2.13 is not on a 0.25 D step. No lab can make it.
        var result = await carts.AddAsync(new AddToCartRequest
        {
            VariantId = variantId,
            PrescriptionDraft = new PrescriptionInput
            {
                Od = new EyeRx { Sphere = -2.13 },
                PdMm = 63,
            },
        });

        Assert.False(result.Ok);
        Assert.Contains("0.25", result.Error!);
    }

    [Fact]
    public async Task A_cylinder_without_an_axis_is_refused()
    {
        fixture.ResetCart();

        var variantId = await fixture.SellableVariantIdAsync(minStock: 1);

        using var scope = fixture.NewScope();

        var carts = scope.ServiceProvider.GetRequiredService<ICartService>();

        var result = await carts.AddAsync(new AddToCartRequest
        {
            VariantId = variantId,
            PrescriptionDraft = new PrescriptionInput
            {
                Od = new EyeRx { Sphere = -2.00, Cylinder = -0.75 }, // no axis
                PdMm = 63,
            },
        });

        Assert.False(result.Ok);
        Assert.Contains("Axis", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Redeeming_a_code_discounts_the_order_and_counts_the_usage()
    {
        var orderId = await PlaceOrderAsync($"promo-{Guid.NewGuid():N}@example.com", promoCode: "WELCOME15");
        var order = await LoadAsync(orderId);

        Assert.True(order.DiscountMinor > 0);
        Assert.Equal("WELCOME15", order.PromoCode);
        Assert.NotNull(order.PromotionId);

        // 15% of the goods, computed in integer minor units.
        var goods = order.SubtotalMinor + order.LensTotalMinor;
        Assert.Equal(Domain.ValueObjects.Money.ApplyBps(goods, 1500), order.DiscountMinor);

        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var promotion = await db.Promotions.AsNoTracking().FirstAsync(p => p.Id == order.PromotionId);

        // Without this a usage cap could never be enforced.
        Assert.True(promotion.UsageCount > 0);
    }

    [Fact]
    public async Task Placing_an_order_consumes_the_bag_but_keeps_the_cart_for_analytics()
    {
        fixture.ResetCart();

        string cartId;
        using (var scope = fixture.NewScope())
        {
            var variantId = await fixture.SellableVariantIdAsync();

            var carts = scope.ServiceProvider.GetRequiredService<ICartService>();
            await carts.AddAsync(new AddToCartRequest { VariantId = variantId, Qty = 1 });
            cartId = (await carts.PeekAsync())!.Id;
        }

        using (var scope = fixture.NewScope())
        {
            var checkout = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
            var outcome = await checkout.PlaceOrderAsync(
                ValidCheckout($"bag-{Guid.NewGuid():N}@example.com"));
            Assert.True(outcome.Ok, outcome.Error);
        }

        using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.True(await db.Carts.AnyAsync(c => c.Id == cartId), "the cart row is kept for analytics");
            Assert.False(await db.CartItems.AnyAsync(i => i.CartId == cartId), "its lines are consumed");
        }
    }

    [Fact]
    public async Task Placing_an_order_is_audited_without_clinical_values()
    {
        var rx = new PrescriptionInput
        {
            Od = new EyeRx { Sphere = -4.75, Cylinder = -1.25, Axis = 90 },
            PdMm = 64,
        };
        var orderId = await PlaceOrderAsync($"audit-{Guid.NewGuid():N}@example.com", rx: rx);

        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entry = await db.AuditLogs.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Action == "order.place" && a.EntityId == orderId);

        Assert.NotNull(entry);
        Assert.Equal("Order", entry!.Entity);

        // The log is read far more widely than the record it describes, so the
        // prescription must not be in it.
        var detail = entry.Detail ?? string.Empty;
        Assert.DoesNotContain("Sphere", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cylinder", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-4.75", detail);
    }

    [Fact]
    public async Task A_shipment_is_opened_and_one_offline_payment_row_is_left()
    {
        var orderId = await PlaceOrderAsync($"ship-{Guid.NewGuid():N}@example.com");
        var order = await LoadAsync(orderId);

        var shipment = Assert.Single(order.Shipments);
        Assert.Equal(ShipmentStatuses.Pending, shipment.Status);
        Assert.Equal(order.ShippingMinor, shipment.CostMinor);

        // Reloading the confirmation page must not leave duplicate pendings.
        var payment = Assert.Single(order.Payments);
        Assert.Equal(PaymentProviders.Cod, payment.Provider);
        Assert.Equal(order.TotalMinor, payment.AmountMinor);
    }

    [Fact]
    public async Task Checkout_refuses_a_payment_method_the_store_has_not_enabled()
    {
        fixture.ResetCart();

        var variantId = await fixture.SellableVariantIdAsync(minStock: 1);

        using var scope = fixture.NewScope();

        var carts = scope.ServiceProvider.GetRequiredService<ICartService>();
        await carts.AddAsync(new AddToCartRequest { VariantId = variantId, Qty = 1 });

        var input = ValidCheckout($"method-{Guid.NewGuid():N}@example.com");
        input.PaymentMethod = "stripe"; // not enabled in this configuration

        var checkout = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
        var outcome = await checkout.PlaceOrderAsync(input);

        Assert.False(outcome.Ok);
        Assert.Contains("isn't available", outcome.Error!);
    }

    [Fact]
    public async Task Checkout_refuses_an_empty_bag()
    {
        fixture.ResetCart();

        using var scope = fixture.NewScope();
        var carts = scope.ServiceProvider.GetRequiredService<ICartService>();
        await carts.GetOrCreateAsync();

        var checkout = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
        var outcome = await checkout.PlaceOrderAsync(
            ValidCheckout($"empty-{Guid.NewGuid():N}@example.com"));

        Assert.False(outcome.Ok);
        Assert.Contains("empty", outcome.Error!, StringComparison.OrdinalIgnoreCase);
    }
}
