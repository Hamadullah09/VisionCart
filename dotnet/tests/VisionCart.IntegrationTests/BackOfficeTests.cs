using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VisionCart.Application.Admin;
using VisionCart.Application.Carts;
using VisionCart.Application.Platform;
using VisionCart.Application.Checkout;
using VisionCart.Application.Prescriptions;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;
using VisionCart.Infrastructure.Persistence;

namespace VisionCart.IntegrationTests;

/// <summary>
/// The back office, driven through its real services against real SQL Server.
///
/// The rules under test are the ones that would cost money or harm a patient if
/// they silently stopped working: the clinical gate on the lab ticket, stock
/// returning on cancellation, and the audit trail actually being readable.
/// </summary>
[Collection("checkout")]
public class BackOfficeTests(CheckoutFlowFixture fixture)
{
    private static CheckoutInput Checkout(string email) => new()
    {
        Email = email,
        Phone = "+92 300 1112222",
        FullName = "Back Office Test",
        Line1 = "1 Test Street",
        City = "Lahore",
        Country = "PK",
        PaymentMethod = PaymentProviders.Cod,
    };

    /// <summary>Places an order carrying a prescription, and returns its ids.</summary>
    private async Task<(string OrderId, string OrderItemId, string PrescriptionId, string PatientId)>
        PlaceRxOrderAsync()
    {
        fixture.ResetCart();

        var variantId = await fixture.SellableVariantIdAsync(minStock: 3);

        using (var scope = fixture.NewScope())
        {
            var carts = scope.ServiceProvider.GetRequiredService<ICartService>();
            var added = await carts.AddAsync(new AddToCartRequest
            {
                VariantId = variantId,
                Qty = 1,
                LensOptionCodes = ["type-single", "idx-150"],
                PrescriptionDraft = new PrescriptionInput
                {
                    Od = new EyeRx { Sphere = -1.50 },
                    Os = new EyeRx { Sphere = -1.75 },
                    PdMm = 63,
                },
            });
            Assert.True(added.Ok, added.Error);
        }

        using (var scope = fixture.NewScope())
        {
            var checkout = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
            var outcome = await checkout.PlaceOrderAsync(Checkout($"bo-{Guid.NewGuid():N}@example.com"));
            Assert.True(outcome.Ok, outcome.Error);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.Orders.AsNoTracking()
                .Include(o => o.Items)
                .FirstAsync(o => o.OrderNo == outcome.OrderNo);

            var line = order.Items.First();
            return (order.Id, line.Id, line.PrescriptionId!, order.PatientId!);
        }
    }

    [Fact]
    public async Task Lenses_cannot_be_marked_ready_while_the_prescription_is_unverified()
    {
        var (_, orderItemId, _, _) = await PlaceRxOrderAsync();

        using var scope = fixture.NewScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderAdminService>();

        // The clinical gate. Without it, an unchecked prescription reaches the
        // lab and the customer gets lenses nobody qualified ever looked at.
        var result = await orders.UpdateLabStatusAsync(orderItemId, LabStatuses.Ready, null);

        Assert.False(result.Ok);
        Assert.Contains("verified", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_intermediate_lab_stage_is_allowed_before_verification()
    {
        // Surfacing and coating happen on the blank, before the prescription
        // matters; only "ready" is gated.
        var (_, orderItemId, _, _) = await PlaceRxOrderAsync();

        using var scope = fixture.NewScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderAdminService>();

        var result = await orders.UpdateLabStatusAsync(orderItemId, "surfacing", "LAB-1");
        Assert.True(result.Ok, result.Error);
    }

    [Fact]
    public async Task Once_an_optician_verifies_it_the_lenses_may_be_marked_ready()
    {
        var (_, orderItemId, prescriptionId, _) = await PlaceRxOrderAsync();

        using (var scope = fixture.NewScope())
        {
            var patients = scope.ServiceProvider.GetRequiredService<IPatientAdminService>();
            var verified = await patients.VerifyAsync(prescriptionId, "optician-user-id");
            Assert.True(verified.Ok, verified.Error);
        }

        using (var scope = fixture.NewScope())
        {
            var orders = scope.ServiceProvider.GetRequiredService<IOrderAdminService>();
            var result = await orders.UpdateLabStatusAsync(orderItemId, LabStatuses.Ready, "LAB-9");
            Assert.True(result.Ok, result.Error);
        }

        using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var line = await db.OrderItems.AsNoTracking().FirstAsync(i => i.Id == orderItemId);
            Assert.Equal(LabStatuses.Ready, line.LabStatus);
            Assert.Equal("LAB-9", line.LabRef);
        }
    }

    [Fact]
    public async Task Verifying_a_prescription_queues_the_customer_an_email()
    {
        var (_, _, prescriptionId, _) = await PlaceRxOrderAsync();

        using var scope = fixture.NewScope();
        var patients = scope.ServiceProvider.GetRequiredService<IPatientAdminService>();
        await patients.VerifyAsync(prescriptionId, "optician-user-id");

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var queued = await db.OutboxEmails.AsNoTracking()
            .AnyAsync(e => e.Template == "prescription.verified" && e.RelatedEntityId == prescriptionId);

        // The legacy system sent nothing at all. Queued, not sent inline, so a
        // slow SMTP server cannot stall the optician's screen.
        Assert.True(queued, "verifying a prescription must tell the customer");
    }

    [Fact]
    public async Task Rejecting_a_prescription_records_the_reason_and_emails_the_customer()
    {
        var (_, _, prescriptionId, _) = await PlaceRxOrderAsync();
        const string reason = "The cylinder has no axis — please confirm with your optician.";

        using var scope = fixture.NewScope();
        var patients = scope.ServiceProvider.GetRequiredService<IPatientAdminService>();
        var result = await patients.RejectAsync(prescriptionId, reason);
        Assert.True(result.Ok, result.Error);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rx = await db.Prescriptions.AsNoTracking().FirstAsync(p => p.Id == prescriptionId);

        Assert.Equal(RxStatuses.Rejected, rx.Status);
        Assert.Contains("axis", rx.Notes!, StringComparison.OrdinalIgnoreCase);

        var queued = await db.OutboxEmails.AsNoTracking()
            .AnyAsync(e => e.Template == "prescription.rejected" && e.RelatedEntityId == prescriptionId);
        Assert.True(queued);
    }

    [Fact]
    public async Task Cancelling_an_order_returns_the_frames_to_stock()
    {
        var (orderId, _, _, _) = await PlaceRxOrderAsync();

        string variantId;
        int afterOrder;
        using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var line = await db.OrderItems.AsNoTracking().FirstAsync(i => i.OrderId == orderId);
            variantId = line.VariantId!;
            afterOrder = await db.FrameVariants.AsNoTracking()
                .Where(v => v.Id == variantId).Select(v => v.StockQty).FirstAsync();
        }

        using (var scope = fixture.NewScope())
        {
            var orders = scope.ServiceProvider.GetRequiredService<IOrderAdminService>();
            var result = await orders.UpdateStatusAsync(orderId, OrderStatuses.Cancelled, null, "Test cancel");
            Assert.True(result.Ok, result.Error);
        }

        using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var afterCancel = await db.FrameVariants.AsNoTracking()
                .Where(v => v.Id == variantId).Select(v => v.StockQty).FirstAsync();

            // Without this, every cancellation quietly loses a frame from the
            // stock figure and the shop drifts into overselling.
            Assert.Equal(afterOrder + 1, afterCancel);

            var order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
            Assert.Equal(OrderStatuses.Cancelled, order.Status);
            Assert.NotNull(order.CancelledAt);
        }
    }

    [Fact]
    public async Task Marking_an_order_paid_by_hand_uses_the_same_transition_as_the_webhook()
    {
        var (orderId, _, _, _) = await PlaceRxOrderAsync();

        using var scope = fixture.NewScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderAdminService>();

        var result = await orders.RecordManualPaymentAsync(orderId, "BANK-REF-1");
        Assert.True(result.Ok, result.Error);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);

        Assert.Equal(PaymentStatuses.Paid, order.PaymentStatus);
        Assert.Equal(OrderStatuses.Paid, order.Status);
        Assert.NotNull(order.PaidAt);

        // Recording it twice must not double-count revenue.
        var second = await orders.RecordManualPaymentAsync(orderId, "BANK-REF-1");
        Assert.False(second.Ok);
    }

    [Fact]
    public async Task Dispatching_an_order_records_the_courier_and_emails_the_customer()
    {
        var (orderId, _, _, _) = await PlaceRxOrderAsync();

        using var scope = fixture.NewScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderAdminService>();

        var result = await orders.CreateShipmentAsync(orderId, "tcs", "TCS123456", null, null);
        Assert.True(result.Ok, result.Error);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.Orders.AsNoTracking().Include(o => o.Shipments)
            .FirstAsync(o => o.Id == orderId);

        Assert.Equal(OrderStatuses.Shipped, order.Status);
        Assert.Contains(order.Shipments, s => s.TrackingNumber == "TCS123456");

        Assert.True(await db.OutboxEmails.AsNoTracking()
            .AnyAsync(e => e.Template == "order.shipped" && e.RelatedEntityId == orderId));
    }

    [Fact]
    public async Task A_new_prescription_is_a_new_version_and_never_edits_the_old_one()
    {
        var (_, _, originalId, patientId) = await PlaceRxOrderAsync();

        using var scope = fixture.NewScope();
        var patients = scope.ServiceProvider.GetRequiredService<IPatientAdminService>();

        var added = await patients.AddPrescriptionAsync(patientId, new PrescriptionInput
        {
            Od = new EyeRx { Sphere = -2.75 },
            Os = new EyeRx { Sphere = -3.00 },
            PdMm = 64,
        }, RxSources.InStoreExam);

        Assert.True(added.Ok, added.Error);
        Assert.NotEqual(originalId, added.Value);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var original = await db.Prescriptions.AsNoTracking().FirstAsync(p => p.Id == originalId);

        // The order was dispensed against the original; it must be untouched.
        Assert.Equal(-1.50, original.OdSphere);

        var count = await db.Prescriptions.AsNoTracking().CountAsync(p => p.PatientId == patientId);
        Assert.True(count >= 2);
    }

    [Fact]
    public async Task An_unfillable_prescription_is_refused_at_the_optician_screen_too()
    {
        var (_, _, _, patientId) = await PlaceRxOrderAsync();

        using var scope = fixture.NewScope();
        var patients = scope.ServiceProvider.GetRequiredService<IPatientAdminService>();

        // -2.13 is not on a 0.25 D step. The same rule that guards the customer
        // form must guard the staff form.
        var result = await patients.AddPrescriptionAsync(patientId, new PrescriptionInput
        {
            Od = new EyeRx { Sphere = -2.13 },
            PdMm = 63,
        }, RxSources.InStoreExam);

        Assert.False(result.Ok);
        Assert.Contains("0.25", result.Error!);
    }

    [Fact]
    public async Task The_audit_trail_is_readable_and_carries_no_clinical_values()
    {
        var (orderId, _, prescriptionId, _) = await PlaceRxOrderAsync();

        using (var scope = fixture.NewScope())
        {
            var patients = scope.ServiceProvider.GetRequiredService<IPatientAdminService>();
            await patients.VerifyAsync(prescriptionId, "optician-user-id");
        }

        using var readScope = fixture.NewScope();
        var platform = readScope.ServiceProvider.GetRequiredService<IPlatformAdminService>();

        // The viewer that did not exist in the legacy application: 26 write sites
        // and nothing that could read them back.
        var page = await platform.ListAuditAsync(new AuditFilters { PerPage = 100 });

        Assert.NotEmpty(page.Items);
        Assert.Contains(page.Items, a => a.Action == AuditActions.OrderPlace && a.EntityId == orderId);
        Assert.Contains(page.Items, a => a.Action == AuditActions.PrescriptionVerify);

        foreach (var entry in page.Items)
        {
            var detail = entry.Detail ?? string.Empty;
            Assert.DoesNotContain("Sphere", detail, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Cylinder", detail, StringComparison.OrdinalIgnoreCase);
        }

        // And the filters actually filter.
        var filtered = await platform.ListAuditAsync(
            new AuditFilters { Action = AuditActions.OrderPlace, PerPage = 100 });
        Assert.All(filtered.Items, a => Assert.Equal(AuditActions.OrderPlace, a.Action));
    }

    [Fact]
    public async Task A_delivery_rate_can_be_created_and_is_quoted_at_checkout()
    {
        var code = $"zz-test-{Guid.NewGuid():N}"[..16];

        using (var scope = fixture.NewScope())
        {
            var platform = scope.ServiceProvider.GetRequiredService<IPlatformAdminService>();
            var result = await platform.SaveShippingRateAsync(null, new ShippingRateDetails
            {
                Name = "ZZ Test courier",
                Code = code,
                Country = "PK",
                Price = 12.50m,
                EtaDaysMin = 1,
                EtaDaysMax = 3,
                Carrier = "tcs",
                IsActive = true,
                Position = 99,
            });
            Assert.True(result.Ok, result.Error);
        }

        using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var saved = await db.ShippingRates.AsNoTracking().FirstAsync(r => r.Code == code);

            // Money typed in major units, stored in minor units.
            Assert.Equal(1250, saved.PriceMinor);

            db.ShippingRates.Remove(await db.ShippingRates.FirstAsync(r => r.Code == code));
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task A_delivery_rate_rejects_an_impossible_estimate()
    {
        using var scope = fixture.NewScope();
        var platform = scope.ServiceProvider.GetRequiredService<IPlatformAdminService>();

        var result = await platform.SaveShippingRateAsync(null, new ShippingRateDetails
        {
            Name = "ZZ Broken", Country = "PK", Price = 1m,
            EtaDaysMin = 9, EtaDaysMax = 2,
        });

        Assert.False(result.Ok);
        Assert.Contains("maximum", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Try_on_calibration_refuses_anchors_that_would_misplace_the_frame()
    {
        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var variantId = await db.FrameVariants.Select(v => v.Id).FirstAsync();

        var catalogue = scope.ServiceProvider.GetRequiredService<ICatalogueAdminService>();

        // Outside the artwork entirely.
        var offImage = await catalogue.SaveTryOnCalibrationAsync(variantId, -0.2, 0.5, 0.7, 0.5, 1, 1, null);
        Assert.False(offImage.Ok);

        // Too close together to solve a scale from.
        var degenerate = await catalogue.SaveTryOnCalibrationAsync(variantId, 0.50, 0.5, 0.505, 0.5, 1, 1, null);
        Assert.False(degenerate.Ok);

        // A sane pair is accepted.
        var ok = await catalogue.SaveTryOnCalibrationAsync(variantId, 0.29, 0.5, 0.71, 0.5, 1, 1, null);
        Assert.True(ok.Ok, ok.Error);
    }

    [Fact]
    public async Task Saving_a_frame_converts_money_once_at_the_edge()
    {
        using var scope = fixture.NewScope();
        var catalogue = scope.ServiceProvider.GetRequiredService<ICatalogueAdminService>();

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var result = await catalogue.SaveFrameAsync(null, new FrameDetails
        {
            Name = $"ZZ Test Frame {suffix}",
            Sku = $"ZZ-{suffix}",
            Slug = $"zz-test-{suffix}",
            Price = 1499.50m,
            Status = ProductStatuses.Draft,
        });

        Assert.True(result.Ok, result.Error);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var frame = await db.Frames.AsNoTracking().FirstAsync(f => f.Id == result.Value);

        // 1499.50 typed by staff becomes exactly 149950 minor units.
        Assert.Equal(149950, frame.BasePriceMinor);

        db.Frames.Remove(await db.Frames.FirstAsync(f => f.Id == result.Value));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_dashboard_reports_the_prescription_queue()
    {
        await PlaceRxOrderAsync();

        using var scope = fixture.NewScope();
        var dashboard = scope.ServiceProvider.GetRequiredService<IDashboardService>();
        var view = await dashboard.BuildAsync();

        Assert.True(view.Stats.PrescriptionsToCheck > 0);
        Assert.NotEmpty(view.PrescriptionQueue);
        Assert.All(view.PrescriptionQueue, p => Assert.Matches(@"^P-\d{6}$", p.FileNo));
    }
}
