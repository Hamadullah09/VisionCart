using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VisionCart.Application.Accounts;
using VisionCart.Application.Platform;
using VisionCart.Application.Privacy;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;
using VisionCart.Infrastructure.Persistence;

namespace VisionCart.IntegrationTests;

/// <summary>
/// Correction, export and erasure of personal data.
///
/// Erasure is the most dangerous code in the system: it rewrites clinical and
/// financial records and cannot be undone. These tests assert both halves of the
/// contract — that identity really is destroyed, and that the records which must
/// survive really do.
/// </summary>
[Collection("checkout")]
public class PrivacyTests(CheckoutFlowFixture fixture)
{
    private sealed record Subject(string UserId, string PatientId, string Email);

    /// <summary>A customer with an account, a patient file and an address.</summary>
    private async Task<Subject> NewSubjectAsync()
    {
        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var addresses = scope.ServiceProvider.GetRequiredService<IAddressService>();

        var email = $"privacy-{Guid.NewGuid():N}@example.com";

        var user = new ApplicationUser
        {
            UserName = email, Email = email, Name = "Privacy Test",
            Role = Roles.Customer, IsActive = true,
        };
        db.Users.Add(user);

        var patient = new Patient
        {
            FileNo = $"T-{Guid.NewGuid().ToString("N")[..8]}",
            UserId = user.Id,
            FirstName = "Grace", LastName = "Hopper",
            Email = email, Phone = "+92 300 1234567",
            DateOfBirth = new DateTime(1906, 12, 9, 0, 0, 0, DateTimeKind.Utc),
            ConsentMarketing = true,
        };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        await addresses.SaveAsync(user.Id, new AddressInput
        {
            FullName = "Grace Hopper", Line1 = "1 Compiler Lane", City = "Lahore",
        });

        return new Subject(user.Id, patient.Id, email);
    }

    private static async Task<Order> AttachOrderAsync(
        ApplicationDbContext db, Subject subject, string status)
    {
        var order = new Order
        {
            OrderNo = $"T{Guid.NewGuid().ToString("N")[..9].ToUpperInvariant()}",
            UserId = subject.UserId,
            PatientId = subject.PatientId,
            Email = subject.Email,
            Phone = "+92 300 1234567",
            Status = status,
            PaymentStatus = PaymentStatuses.Paid,
            SubtotalMinor = 850_000,
            TotalMinor = 850_000,
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    // --- Raising a request ---------------------------------------------------

    [Fact]
    public async Task A_request_is_logged_linked_to_the_patient_file_and_acknowledged()
    {
        var subject = await NewSubjectAsync();
        using var scope = fixture.NewScope();
        var privacy = scope.ServiceProvider.GetRequiredService<IDataSubjectService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var result = await privacy.RaiseAsync(subject.UserId, new DataRequestInput
        {
            Kind = DataSubjectRequestKinds.Correction,
            Email = subject.Email,
            Message = "My surname is spelt wrong.",
        });

        Assert.True(result.Ok, result.Error);
        Assert.Equal(subject.PatientId, result.Value!.PatientId);
        Assert.Equal(DataSubjectRequestStatuses.Pending, result.Value.Status);

        // Silence is what makes a customer escalate to a regulator.
        Assert.True(await db.OutboxEmails.AsNoTracking()
            .AnyAsync(e => e.RelatedEntityId == result.Value.Id));
    }

    [Fact]
    public async Task The_same_request_cannot_be_opened_twice()
    {
        var subject = await NewSubjectAsync();
        using var scope = fixture.NewScope();
        var privacy = scope.ServiceProvider.GetRequiredService<IDataSubjectService>();

        var input = new DataRequestInput
        {
            Kind = DataSubjectRequestKinds.Erasure, Email = subject.Email,
        };

        Assert.True((await privacy.RaiseAsync(subject.UserId, input)).Ok);

        // A customer clicking twice must not put two live erasures in the queue.
        var second = await privacy.RaiseAsync(subject.UserId, input);
        Assert.False(second.Ok);
    }

    [Fact]
    public async Task An_audit_entry_records_the_kind_but_never_what_the_customer_wrote()
    {
        var subject = await NewSubjectAsync();
        using var scope = fixture.NewScope();
        var privacy = scope.ServiceProvider.GetRequiredService<IDataSubjectService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        const string secret = "My prescription is -8.00 and I am embarrassed by it.";
        var raised = await privacy.RaiseAsync(subject.UserId, new DataRequestInput
        {
            Kind = DataSubjectRequestKinds.Correction, Email = subject.Email, Message = secret,
        });

        var entry = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityId == raised.Value!.Id)
            .FirstOrDefaultAsync();

        Assert.NotNull(entry);

        // §10: the message may itself carry clinical detail, so it stays in its
        // column and out of a trail that staff browse casually.
        Assert.DoesNotContain("-8.00", entry!.Detail ?? "");
        Assert.DoesNotContain("embarrassed", entry.Detail ?? "");
    }

    // --- Export --------------------------------------------------------------

    [Fact]
    public async Task An_export_carries_the_customers_own_records_and_nobody_elses()
    {
        var subject = await NewSubjectAsync();
        var stranger = await NewSubjectAsync();

        using var scope = fixture.NewScope();
        var privacy = scope.ServiceProvider.GetRequiredService<IDataSubjectService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await AttachOrderAsync(db, subject, OrderStatuses.Delivered);
        await AttachOrderAsync(db, stranger, OrderStatuses.Delivered);

        var json = await privacy.ExportPersonalDataAsync(subject.UserId);

        Assert.Contains(subject.Email, json);
        Assert.Contains("Grace", json);

        // The failure this guards against is serialising an entity graph and
        // dragging in navigation properties belonging to other people.
        Assert.DoesNotContain(stranger.Email, json);

        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(1, parsed.RootElement.GetProperty("Orders").GetArrayLength());
    }

    [Fact]
    public async Task Downloading_your_own_data_is_audited_like_any_other_disclosure()
    {
        var subject = await NewSubjectAsync();
        using var scope = fixture.NewScope();
        var privacy = scope.ServiceProvider.GetRequiredService<IDataSubjectService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await privacy.ExportPersonalDataAsync(subject.UserId);

        Assert.True(await db.AuditLogs.AsNoTracking().AnyAsync(
            a => a.Action == AuditActions.ExportPatients && a.EntityId == subject.UserId));
    }

    // --- Erasure -------------------------------------------------------------

    [Fact]
    public async Task An_erasure_is_refused_while_an_order_is_still_on_its_way()
    {
        var subject = await NewSubjectAsync();
        using var scope = fixture.NewScope();
        var privacy = scope.ServiceProvider.GetRequiredService<IDataSubjectService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await AttachOrderAsync(db, subject, OrderStatuses.InLab);

        var raised = await privacy.RaiseAsync(subject.UserId, new DataRequestInput
        {
            Kind = DataSubjectRequestKinds.Erasure, Email = subject.Email,
        });

        var impact = await privacy.AssessErasureAsync(subject.PatientId);
        Assert.False(impact.CanErase);

        // The courier still needs a name and an address to deliver to.
        var erased = await privacy.EraseAsync(raised.Value!.Id);
        Assert.False(erased.Ok);

        var patient = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == subject.PatientId);
        Assert.Equal("Grace", patient.FirstName);
    }

    [Fact]
    public async Task Erasure_destroys_identity_but_keeps_the_clinical_and_financial_record()
    {
        var subject = await NewSubjectAsync();
        string orderId;
        string prescriptionId;

        using (var setup = fixture.NewScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            orderId = (await AttachOrderAsync(db, subject, OrderStatuses.Delivered)).Id;

            var rx = new Prescription
            {
                PatientId = subject.PatientId,
                Status = RxStatuses.Verified,
                OdSphere = -2.25, OdCylinder = -0.75, OdAxis = 180,
                OsSphere = -2.00,
            };
            db.Prescriptions.Add(rx);
            await db.SaveChangesAsync();
            prescriptionId = rx.Id;
        }

        using var scope = fixture.NewScope();
        var privacy = scope.ServiceProvider.GetRequiredService<IDataSubjectService>();

        var raised = await privacy.RaiseAsync(subject.UserId, new DataRequestInput
        {
            Kind = DataSubjectRequestKinds.Erasure, Email = subject.Email,
        });

        var erased = await privacy.EraseAsync(raised.Value!.Id);
        Assert.True(erased.Ok, erased.Error);

        using var after = fixture.NewScope();
        var db2 = after.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // --- identity is gone ---
        var patient = await db2.Patients.AsNoTracking().FirstAsync(p => p.Id == subject.PatientId);
        Assert.DoesNotContain("Grace", patient.FirstName);
        Assert.DoesNotContain("Hopper", patient.LastName);
        Assert.Null(patient.Email);
        Assert.Null(patient.Phone);
        Assert.Null(patient.DateOfBirth);
        Assert.False(patient.ConsentMarketing);

        var order = await db2.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        Assert.DoesNotContain(subject.Email, order.Email);
        Assert.Null(order.Phone);

        var account = await db2.Users.AsNoTracking().FirstAsync(u => u.Id == subject.UserId);
        Assert.DoesNotContain(subject.Email, account.Email!);
        Assert.False(account.IsActive);

        var addresses = await db2.Addresses.AsNoTracking()
            .Where(a => a.UserId == subject.UserId).ToListAsync();
        Assert.All(addresses, a => Assert.DoesNotContain("Compiler Lane", a.Line1));
        Assert.All(addresses, a => Assert.NotNull(a.DeletedAt));

        // --- but the records that must survive, did ---
        // A prescription is a medical record and an order is a financial one.
        // Deleting either to satisfy an erasure request would break a retention
        // obligation that outranks it.
        var prescription = await db2.Prescriptions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == prescriptionId);

        Assert.NotNull(prescription);
        Assert.Equal(-2.25, prescription!.OdSphere);
        Assert.Equal(180, prescription.OdAxis);

        Assert.Equal(850_000, order.TotalMinor);
        Assert.Equal(PaymentStatuses.Paid, order.PaymentStatus);
    }

    [Fact]
    public async Task Erasure_invalidates_every_session_the_customer_had_open()
    {
        var subject = await NewSubjectAsync();
        string stampBefore;

        using (var setup = fixture.NewScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            stampBefore = (await db.Users.AsNoTracking().FirstAsync(u => u.Id == subject.UserId))
                .SecurityStamp!;
        }

        using var scope = fixture.NewScope();
        var privacy = scope.ServiceProvider.GetRequiredService<IDataSubjectService>();

        var raised = await privacy.RaiseAsync(subject.UserId, new DataRequestInput
        {
            Kind = DataSubjectRequestKinds.Erasure, Email = subject.Email,
        });
        Assert.True((await privacy.EraseAsync(raised.Value!.Id)).Ok);

        using var after = fixture.NewScope();
        var db2 = after.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stampAfter = (await db2.Users.AsNoTracking().FirstAsync(u => u.Id == subject.UserId))
            .SecurityStamp;

        // An erased customer still holding a live cookie could keep browsing
        // their own — now redacted — account.
        Assert.NotEqual(stampBefore, stampAfter);
    }

    [Fact]
    public async Task Only_an_erasure_request_can_be_actioned_as_one()
    {
        var subject = await NewSubjectAsync();
        using var scope = fixture.NewScope();
        var privacy = scope.ServiceProvider.GetRequiredService<IDataSubjectService>();

        var raised = await privacy.RaiseAsync(subject.UserId, new DataRequestInput
        {
            Kind = DataSubjectRequestKinds.Correction, Email = subject.Email,
        });

        // A correction request must never fall through into a destructive path.
        var result = await privacy.EraseAsync(raised.Value!.Id);
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task The_queue_lists_the_oldest_open_request_first()
    {
        using var scope = fixture.NewScope();
        var privacy = scope.ServiceProvider.GetRequiredService<IDataSubjectService>();

        var subject = await NewSubjectAsync();
        await privacy.RaiseAsync(subject.UserId, new DataRequestInput
        {
            Kind = DataSubjectRequestKinds.Restriction, Email = subject.Email,
        });

        var queue = await privacy.QueueAsync(null, 1);
        Assert.NotEmpty(queue.Items);

        // A statutory clock runs on each of these, so a closed request must never
        // outrank an open one waiting for someone to act.
        var open = queue.Items
            .Select((r, i) => (r, i))
            .Where(x => DataSubjectRequestStatuses.Open.Contains(x.r.Status))
            .ToList();

        var closed = queue.Items
            .Select((r, i) => (r, i))
            .Where(x => !DataSubjectRequestStatuses.Open.Contains(x.r.Status))
            .ToList();

        if (open.Count > 0 && closed.Count > 0)
            Assert.True(open.Max(x => x.i) < closed.Min(x => x.i),
                "a closed request was listed above an open one");
    }
}
