using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VisionCart.Application.Accounts;
using VisionCart.Application.Appointments;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;
using VisionCart.Infrastructure.Persistence;

namespace VisionCart.IntegrationTests;

/// <summary>
/// The customer address book.
///
/// The tests that matter here are about ownership, not CRUD: an address book
/// keyed only by id lets one customer read and delete another's, and the ids are
/// guessable enough for that to be a real hole rather than a theoretical one.
/// </summary>
[Collection("checkout")]
public class AddressBookTests(CheckoutFlowFixture fixture)
{
    private static AddressInput Valid(string name = "Ada Lovelace") => new()
    {
        FullName = name,
        Line1 = "12 Analytical Way",
        City = "Lahore",
        State = "Punjab",
        PostalCode = "54000",
        Country = "pk",
    };

    private async Task<string> NewUserAsync()
    {
        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // No explicit Id: the entity's constructor assigns a Cuid, which is what
        // the 30-character key column is sized for.
        var user = new ApplicationUser
        {
            UserName = $"addr-{Guid.NewGuid():N}@example.com",
            Email = $"addr-{Guid.NewGuid():N}@example.com",
            Name = "Address Test",
            Role = Roles.Customer,
            IsActive = true,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task The_first_address_saved_becomes_the_default_without_being_asked()
    {
        var userId = await NewUserAsync();
        using var scope = fixture.NewScope();
        var addresses = scope.ServiceProvider.GetRequiredService<IAddressService>();

        var saved = await addresses.SaveAsync(userId, Valid());
        Assert.True(saved.Ok, saved.Error);

        var book = await addresses.ListAsync(userId);

        // Otherwise checkout opens with nothing selected for a customer who has
        // exactly one address, which is most of them.
        Assert.Single(book);
        Assert.True(book[0].IsDefault);
        Assert.Equal("PK", book[0].Country);
    }

    [Fact]
    public async Task Choosing_a_new_default_clears_the_old_one()
    {
        var userId = await NewUserAsync();
        using var scope = fixture.NewScope();
        var addresses = scope.ServiceProvider.GetRequiredService<IAddressService>();

        await addresses.SaveAsync(userId, Valid("First"));
        await addresses.SaveAsync(userId, Valid("Second"));

        var second = (await addresses.ListAsync(userId)).First(a => a.FullName == "Second");
        var result = await addresses.MakeDefaultAsync(userId, second.Id);
        Assert.True(result.Ok, result.Error);

        var book = await addresses.ListAsync(userId);

        // Two defaults is worse than none: checkout would pick one arbitrarily.
        Assert.Single(book, a => a.IsDefault);
        Assert.Equal("Second", book.Single(a => a.IsDefault).FullName);
    }

    [Fact]
    public async Task One_customer_cannot_read_or_delete_anothers_address()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();

        using var scope = fixture.NewScope();
        var addresses = scope.ServiceProvider.GetRequiredService<IAddressService>();

        await addresses.SaveAsync(owner, Valid("Owner"));
        var theirs = (await addresses.ListAsync(owner)).Single();

        Assert.Null(await addresses.FindAsync(stranger, theirs.Id));

        var delete = await addresses.DeleteAsync(stranger, theirs.Id);
        Assert.False(delete.Ok);

        var edit = await addresses.SaveAsync(stranger, new AddressInput
        {
            Id = theirs.Id, FullName = "Hijacked", Line1 = "x", City = "y",
        });
        Assert.False(edit.Ok);

        // Still there, still theirs, still untouched.
        var after = await addresses.FindAsync(owner, theirs.Id);
        Assert.NotNull(after);
        Assert.Equal("Owner", after!.FullName);
    }

    [Fact]
    public async Task Removing_an_address_hides_it_without_erasing_where_an_order_went()
    {
        var userId = await NewUserAsync();
        using var scope = fixture.NewScope();
        var addresses = scope.ServiceProvider.GetRequiredService<IAddressService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await addresses.SaveAsync(userId, Valid());
        var address = (await addresses.ListAsync(userId)).Single();

        var deleted = await addresses.DeleteAsync(userId, address.Id);
        Assert.True(deleted.Ok, deleted.Error);

        Assert.Empty(await addresses.ListAsync(userId));

        // A hard delete would take the delivery record of every past order with
        // it, and the foreign key would refuse anyway.
        var row = await db.Addresses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == address.Id);
        Assert.NotNull(row);
        Assert.NotNull(row!.DeletedAt);
        Assert.Equal("12 Analytical Way", row.Line1);
    }

    [Fact]
    public async Task Removing_the_default_promotes_another_one()
    {
        var userId = await NewUserAsync();
        using var scope = fixture.NewScope();
        var addresses = scope.ServiceProvider.GetRequiredService<IAddressService>();

        await addresses.SaveAsync(userId, Valid("First"));
        await addresses.SaveAsync(userId, Valid("Second"));

        var current = (await addresses.ListAsync(userId)).Single(a => a.IsDefault);
        await addresses.DeleteAsync(userId, current.Id);

        var book = await addresses.ListAsync(userId);

        // Leaving the book without a default would open checkout empty for a
        // customer who still has an address.
        Assert.Single(book);
        Assert.True(book[0].IsDefault);
    }

    [Fact]
    public async Task An_address_without_a_street_or_a_city_is_refused_by_field()
    {
        var userId = await NewUserAsync();
        using var scope = fixture.NewScope();
        var addresses = scope.ServiceProvider.GetRequiredService<IAddressService>();

        var result = await addresses.SaveAsync(userId, new AddressInput { FullName = "No Address" });

        Assert.False(result.Ok);
        Assert.Contains("Line1", result.FieldErrors.Keys);
        Assert.Contains("City", result.FieldErrors.Keys);
        Assert.Empty(await addresses.ListAsync(userId));
    }
}

/// <summary>
/// The clinic diary. The rules under test are the ones that cost a practice real
/// money when they fail: a double booking, or a slot sold in the past.
/// </summary>
[Collection("checkout")]
public class AppointmentTests(CheckoutFlowFixture fixture) : IAsyncLifetime
{
    /// <summary>
    /// Clears the diary of anything a previous test left behind.
    ///
    /// Every booking is a real row, and nothing removes it — so a test that
    /// claimed a fixed slot passed the first time and failed on every run after,
    /// with "that slot has just been taken". The slot a test needs is part of its
    /// arrange step, exactly as stock is for checkout.
    ///
    /// Only rows belonging to this suite's own patients are touched: their file
    /// numbers carry a T- prefix that the sequential P-000000 series never uses.
    /// </summary>
    public async Task InitializeAsync()
    {
        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var stale = await db.Appointments
            .Where(a => db.Patients.Any(p => p.Id == a.PatientId && p.FileNo.StartsWith("T-")))
            .ToListAsync();

        if (stale.Count == 0) return;

        db.Appointments.RemoveRange(stale);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>A Monday well inside the bookable window, at 11:00.</summary>
    private static DateTime NextOpenSlot(int hour = 11, int dayOffset = 0)
    {
        var day = DateTime.UtcNow.Date.AddDays(7 + dayOffset);
        while (day.DayOfWeek is DayOfWeek.Sunday) day = day.AddDays(1);

        return DateTime.SpecifyKind(day.AddHours(hour), DateTimeKind.Utc);
    }

    private async Task<string> NewPatientAsync()
    {
        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var patient = new Patient
        {
            FileNo = $"T-{Guid.NewGuid().ToString("N")[..8]}",
            FirstName = "Diary",
            LastName = "Test",
            Email = $"diary-{Guid.NewGuid():N}@example.com",
        };

        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        return patient.Id;
    }

    [Fact]
    public async Task A_booking_is_confirmed_and_the_patient_is_emailed()
    {
        var patientId = await NewPatientAsync();
        using var scope = fixture.NewScope();
        var appointments = scope.ServiceProvider.GetRequiredService<IAppointmentService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var result = await appointments.BookAsync(new BookingInput
        {
            PatientId = patientId, StartsAt = NextOpenSlot(), Kind = AppointmentKinds.EyeTest,
        });

        Assert.True(result.Ok, result.Error);
        Assert.Equal(AppointmentStatuses.Scheduled, result.Value!.Status);

        // A confirmation nobody receives is the legacy system's failure mode.
        var queued = await db.OutboxEmails.AsNoTracking()
            .AnyAsync(e => e.RelatedEntityId == result.Value.Id);

        Assert.True(queued, "no confirmation email was queued");
    }

    [Fact]
    public async Task The_same_slot_cannot_be_sold_twice()
    {
        var first = await NewPatientAsync();
        var second = await NewPatientAsync();
        var slot = NextOpenSlot(12, dayOffset: 1);

        using var scope = fixture.NewScope();
        var appointments = scope.ServiceProvider.GetRequiredService<IAppointmentService>();

        var one = await appointments.BookAsync(new BookingInput { PatientId = first, StartsAt = slot });
        Assert.True(one.Ok, one.Error);

        // Two people in one chair is the failure a diary exists to prevent.
        var two = await appointments.BookAsync(new BookingInput { PatientId = second, StartsAt = slot });
        Assert.False(two.Ok);
        Assert.Contains("taken", two.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_overlapping_booking_is_refused_even_when_it_does_not_start_at_the_same_minute()
    {
        var first = await NewPatientAsync();
        var second = await NewPatientAsync();
        var slot = NextOpenSlot(14, dayOffset: 2);

        using var scope = fixture.NewScope();
        var appointments = scope.ServiceProvider.GetRequiredService<IAppointmentService>();

        await appointments.BookAsync(new BookingInput
        {
            PatientId = first, StartsAt = slot, Minutes = 60,
        });

        // Starts 30 minutes in — a naive equality check on the start time would
        // wave this through and the second patient would be kept waiting.
        var overlapping = await appointments.BookAsync(new BookingInput
        {
            PatientId = second, StartsAt = slot.AddMinutes(30), Minutes = 30,
        });

        Assert.False(overlapping.Ok);
    }

    [Fact]
    public async Task Cancelling_frees_the_slot_again()
    {
        var first = await NewPatientAsync();
        var second = await NewPatientAsync();
        var slot = NextOpenSlot(15, dayOffset: 3);

        using var scope = fixture.NewScope();
        var appointments = scope.ServiceProvider.GetRequiredService<IAppointmentService>();

        var booked = await appointments.BookAsync(new BookingInput { PatientId = first, StartsAt = slot });
        Assert.True(booked.Ok, booked.Error);

        var cancelled = await appointments.CancelAsync(booked.Value!.Id, "Changed their mind");
        Assert.True(cancelled.Ok, cancelled.Error);

        var rebooked = await appointments.BookAsync(new BookingInput { PatientId = second, StartsAt = slot });
        Assert.True(rebooked.Ok, rebooked.Error);
    }

    [Fact]
    public async Task Nothing_can_be_booked_in_the_past_or_outside_opening_hours()
    {
        var patientId = await NewPatientAsync();
        using var scope = fixture.NewScope();
        var appointments = scope.ServiceProvider.GetRequiredService<IAppointmentService>();

        var past = await appointments.BookAsync(new BookingInput
        {
            PatientId = patientId, StartsAt = DateTime.UtcNow.AddDays(-1),
        });
        Assert.False(past.Ok);

        var tooEarly = await appointments.BookAsync(new BookingInput
        {
            PatientId = patientId, StartsAt = NextOpenSlot(7, dayOffset: 4),
        });
        Assert.False(tooEarly.Ok);

        // 17:45 + 30 minutes runs past an 18:00 close — the end matters, not just
        // the start.
        var runsPastClosing = await appointments.BookAsync(new BookingInput
        {
            PatientId = patientId,
            StartsAt = NextOpenSlot(17, dayOffset: 4).AddMinutes(45),
            Minutes = 30,
        });
        Assert.False(runsPastClosing.Ok);
    }

    [Fact]
    public async Task A_slot_that_is_taken_is_offered_as_unavailable_rather_than_hidden()
    {
        var patientId = await NewPatientAsync();
        var slot = NextOpenSlot(13, dayOffset: 5);

        using var scope = fixture.NewScope();
        var appointments = scope.ServiceProvider.GetRequiredService<IAppointmentService>();

        await appointments.BookAsync(new BookingInput { PatientId = patientId, StartsAt = slot });

        var slots = await appointments.SlotsForDayAsync(DateOnly.FromDateTime(slot));
        var taken = slots.SingleOrDefault(s => s.StartsAt == slot);

        Assert.NotNull(taken);
        Assert.False(taken!.Available);

        // An empty grid reads as broken; a struck-through one reads as busy.
        Assert.Contains(slots, s => s.Available);
    }

    [Fact]
    public async Task Sunday_offers_no_slots_at_all()
    {
        using var scope = fixture.NewScope();
        var appointments = scope.ServiceProvider.GetRequiredService<IAppointmentService>();

        var sunday = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1));
        while (sunday.DayOfWeek != DayOfWeek.Sunday) sunday = sunday.AddDays(1);

        Assert.Empty(await appointments.SlotsForDayAsync(sunday));
    }

    [Fact]
    public async Task An_appointment_cannot_be_marked_as_seen_before_it_has_happened()
    {
        var patientId = await NewPatientAsync();
        using var scope = fixture.NewScope();
        var appointments = scope.ServiceProvider.GetRequiredService<IAppointmentService>();

        var booked = await appointments.BookAsync(new BookingInput
        {
            PatientId = patientId, StartsAt = NextOpenSlot(16, dayOffset: 6),
        });

        var result = await appointments.SetStatusAsync(booked.Value!.Id, AppointmentStatuses.Completed);

        Assert.False(result.Ok);
        Assert.Contains("not happened", result.Error!, StringComparison.OrdinalIgnoreCase);
    }
}
