using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Common;
using VisionCart.Application.Email;
using VisionCart.Application.Platform;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;

namespace VisionCart.Application.Appointments;

public sealed record SlotOption(DateTime StartsAt, bool Available);

public sealed record DiaryDay(DateOnly Date, IReadOnlyList<Appointment> Appointments);

public sealed class BookingInput
{
    public string PatientId { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public string Kind { get; set; } = AppointmentKinds.EyeTest;
    public int Minutes { get; set; } = 30;
    public string? StaffUserId { get; set; }
    public string? Notes { get; set; }
}

public interface IAppointmentService
{
    Task<IReadOnlyList<SlotOption>> SlotsForDayAsync(
        DateOnly day, string? staffUserId = null, int minutes = 30, CancellationToken ct = default);

    Task<ActionResult<Appointment>> BookAsync(BookingInput input, CancellationToken ct = default);
    Task<ActionResult> RescheduleAsync(string id, DateTime newStart, CancellationToken ct = default);
    Task<ActionResult> CancelAsync(string id, string? reason, CancellationToken ct = default);
    Task<ActionResult> SetStatusAsync(string id, string status, CancellationToken ct = default);

    Task<IReadOnlyList<Appointment>> ForPatientAsync(string patientId, CancellationToken ct = default);
    Task<IReadOnlyList<DiaryDay>> DiaryAsync(DateOnly from, int days, CancellationToken ct = default);
    Task<Appointment?> FindAsync(string id, CancellationToken ct = default);
}

/// <summary>
/// The clinic diary.
///
/// The legacy application had an <c>Appointment</c> table and no code that ever
/// wrote to it, so this is new behaviour rather than a port. The rules it
/// enforces are the ones a practice actually needs:
///
/// <list type="bullet">
/// <item>A slot cannot be double-booked for the same optician. Two people in one
/// chair is the failure that costs a practice a customer.</item>
/// <item>Nothing may be booked in the past.</item>
/// <item>A cancelled appointment frees its slot; a completed one does not, since
/// it really did happen.</item>
/// <item>A patient cannot hold two open appointments at the same moment, however
/// many opticians are free.</item>
/// </list>
///
/// Times are stored and compared in UTC. The clinic's opening hours are applied
/// in local time and converted at the edge — a diary that drifts by an hour
/// twice a year is worse than no diary at all.
/// </summary>
public sealed class AppointmentService(
    IApplicationDbContext db,
    IAuditService audit,
    IEmailService email) : IAppointmentService
{
    /// <summary>Opening hours, local clinic time.</summary>
    private static readonly TimeOnly OpensAt = new(10, 0);
    private static readonly TimeOnly ClosesAt = new(18, 0);
    private const int SlotStepMinutes = 30;

    /// <summary>How far ahead the public booking form will look.</summary>
    public const int BookableDaysAhead = 60;

    public async Task<IReadOnlyList<SlotOption>> SlotsForDayAsync(
        DateOnly day, string? staffUserId = null, int minutes = 30, CancellationToken ct = default)
    {
        var dayStart = day.ToDateTime(OpensAt, DateTimeKind.Utc);
        var dayEnd = day.ToDateTime(ClosesAt, DateTimeKind.Utc);

        // Sunday is closed. A booking form that offers a closed day generates a
        // phone call, which is exactly what it was supposed to save.
        if (day.DayOfWeek == DayOfWeek.Sunday) return [];

        var taken = await db.Appointments
            .Where(a => a.StartsAt >= dayStart.AddHours(-4)
                        && a.StartsAt <= dayEnd.AddHours(4)
                        && a.Status != AppointmentStatuses.Cancelled
                        && (staffUserId == null || a.StaffUserId == staffUserId))
            .Select(a => new { a.StartsAt, a.Minutes })
            .ToListAsync(ct);

        var slots = new List<SlotOption>();
        var now = DateTime.UtcNow;

        for (var at = dayStart; at.AddMinutes(minutes) <= dayEnd; at = at.AddMinutes(SlotStepMinutes))
        {
            var end = at.AddMinutes(minutes);

            var clashes = taken.Any(t => at < t.StartsAt.AddMinutes(t.Minutes) && t.StartsAt < end);
            slots.Add(new SlotOption(at, !clashes && at > now));
        }

        return slots;
    }

    public async Task<ActionResult<Appointment>> BookAsync(
        BookingInput input, CancellationToken ct = default)
    {
        if (!AppointmentKinds.All.Contains(input.Kind))
            return ActionResult<Appointment>.Fail("Choose a type of appointment.");

        if (input.Minutes is < 10 or > 180)
            return ActionResult<Appointment>.Fail("An appointment runs between 10 minutes and 3 hours.");

        var startsAt = DateTime.SpecifyKind(input.StartsAt, DateTimeKind.Utc);

        if (startsAt <= DateTime.UtcNow)
            return ActionResult<Appointment>.Fail("That time has already passed. Choose a later slot.");

        if (startsAt > DateTime.UtcNow.AddDays(BookableDaysAhead))
            return ActionResult<Appointment>.Fail(
                $"Appointments open {BookableDaysAhead} days ahead. Choose an earlier date.");

        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == input.PatientId, ct);
        if (patient is null) return ActionResult<Appointment>.Fail("That patient file no longer exists.");

        var withinHours = IsWithinOpeningHours(startsAt, input.Minutes);
        if (!withinHours)
            return ActionResult<Appointment>.Fail(
                $"We are open {OpensAt:HH\\:mm}–{ClosesAt:HH\\:mm}, Monday to Saturday.");

        var clash = await ClashAsync(startsAt, input.Minutes, input.StaffUserId, null, ct);
        if (clash is not null) return ActionResult<Appointment>.Fail(clash);

        var patientClash = await db.Appointments.AnyAsync(
            a => a.PatientId == input.PatientId
                 && a.Status == AppointmentStatuses.Scheduled
                 && a.StartsAt == startsAt, ct);

        if (patientClash)
            return ActionResult<Appointment>.Fail("This patient already has an appointment at that time.");

        var appointment = new Appointment
        {
            PatientId = input.PatientId,
            StartsAt = startsAt,
            Minutes = input.Minutes,
            Kind = input.Kind,
            Status = AppointmentStatuses.Scheduled,
            StaffUserId = string.IsNullOrWhiteSpace(input.StaffUserId) ? null : input.StaffUserId,
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
        };

        db.Appointments.Add(appointment);
        await db.SaveChangesAsync(ct);

        // The audit detail names the slot and the kind, never the reason for the
        // visit — "follow_up" is a category, a clinical note is not (§10).
        await audit.WriteAsync(AuditActions.AppointmentBook, "Appointment", appointment.Id,
            new { appointment.StartsAt, appointment.Kind, appointment.Minutes }, ct);

        await QueueConfirmationAsync(appointment, patient, "booked", ct);

        return ActionResult<Appointment>.Success(appointment);
    }

    public async Task<ActionResult> RescheduleAsync(
        string id, DateTime newStart, CancellationToken ct = default)
    {
        var appointment = await FindAsync(id, ct);
        if (appointment is null) return ActionResult.Fail("That appointment no longer exists.");

        if (appointment.Status != AppointmentStatuses.Scheduled)
            return ActionResult.Fail("Only a scheduled appointment can be moved.");

        var startsAt = DateTime.SpecifyKind(newStart, DateTimeKind.Utc);
        if (startsAt <= DateTime.UtcNow) return ActionResult.Fail("That time has already passed.");

        if (!IsWithinOpeningHours(startsAt, appointment.Minutes))
            return ActionResult.Fail($"We are open {OpensAt:HH\\:mm}–{ClosesAt:HH\\:mm}, Monday to Saturday.");

        var clash = await ClashAsync(startsAt, appointment.Minutes, appointment.StaffUserId, appointment.Id, ct);
        if (clash is not null) return ActionResult.Fail(clash);

        var previous = appointment.StartsAt;
        appointment.StartsAt = startsAt;
        appointment.UpdatedAt = DateTime.UtcNow;

        // A moved appointment needs a fresh reminder; the old one described a
        // time that no longer exists.
        appointment.ReminderSentAt = null;

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditActions.AppointmentReschedule, "Appointment", appointment.Id,
            new { From = previous, To = startsAt }, ct);

        var patient = await db.Patients.FirstAsync(p => p.Id == appointment.PatientId, ct);
        await QueueConfirmationAsync(appointment, patient, "moved", ct);

        return ActionResult.Success();
    }

    public async Task<ActionResult> CancelAsync(
        string id, string? reason, CancellationToken ct = default)
    {
        var appointment = await FindAsync(id, ct);
        if (appointment is null) return ActionResult.Fail("That appointment no longer exists.");

        if (appointment.Status == AppointmentStatuses.Cancelled)
            return ActionResult.Fail("That appointment is already cancelled.");

        if (appointment.Status == AppointmentStatuses.Completed)
            return ActionResult.Fail("A completed appointment cannot be cancelled.");

        appointment.Status = AppointmentStatuses.Cancelled;
        appointment.CancelledAt = DateTime.UtcNow;
        appointment.CancelledReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        appointment.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditActions.AppointmentCancel, "Appointment", appointment.Id, null, ct);

        var patient = await db.Patients.FirstAsync(p => p.Id == appointment.PatientId, ct);
        await QueueConfirmationAsync(appointment, patient, "cancelled", ct);

        return ActionResult.Success();
    }

    public async Task<ActionResult> SetStatusAsync(
        string id, string status, CancellationToken ct = default)
    {
        if (!AppointmentStatuses.All.Contains(status))
            return ActionResult.Fail("That is not a valid appointment status.");

        var appointment = await FindAsync(id, ct);
        if (appointment is null) return ActionResult.Fail("That appointment no longer exists.");

        if (status == AppointmentStatuses.Completed && appointment.StartsAt > DateTime.UtcNow)
            return ActionResult.Fail("That appointment has not happened yet.");

        appointment.Status = status;
        appointment.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditActions.AppointmentComplete, "Appointment", appointment.Id,
            new { Status = status }, ct);

        return ActionResult.Success();
    }

    public Task<Appointment?> FindAsync(string id, CancellationToken ct = default) =>
        db.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyList<Appointment>> ForPatientAsync(
        string patientId, CancellationToken ct = default) =>
        await db.Appointments
            .Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.StartsAt)
            .Take(50)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DiaryDay>> DiaryAsync(
        DateOnly from, int days, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 31);
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = from.AddDays(days).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var appointments = await db.Appointments
            .Include(a => a.Patient)
            .Where(a => a.StartsAt >= start && a.StartsAt < end)
            .OrderBy(a => a.StartsAt)
            .ToListAsync(ct);

        return Enumerable.Range(0, days)
            .Select(offset =>
            {
                var day = from.AddDays(offset);
                return new DiaryDay(day,
                    appointments.Where(a => DateOnly.FromDateTime(a.StartsAt) == day).ToList());
            })
            .ToList();
    }

    /// <summary>Returns a message when the slot is taken, or null when it is free.</summary>
    private async Task<string?> ClashAsync(
        DateTime startsAt, int minutes, string? staffUserId, string? ignoreId, CancellationToken ct)
    {
        var end = startsAt.AddMinutes(minutes);

        var clash = await db.Appointments.AnyAsync(
            a => a.Id != ignoreId
                 && a.Status != AppointmentStatuses.Cancelled
                 && a.StaffUserId == staffUserId
                 && startsAt < a.StartsAt.AddMinutes(a.Minutes)
                 && a.StartsAt < end, ct);

        return clash ? "That slot has just been taken. Choose another." : null;
    }

    private static bool IsWithinOpeningHours(DateTime startsAt, int minutes)
    {
        if (startsAt.DayOfWeek == DayOfWeek.Sunday) return false;

        var open = startsAt.Date.Add(OpensAt.ToTimeSpan());
        var close = startsAt.Date.Add(ClosesAt.ToTimeSpan());

        return startsAt >= open && startsAt.AddMinutes(minutes) <= close;
    }

    private async Task QueueConfirmationAsync(
        Appointment appointment, Patient patient, string what, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(patient.Email)) return;

        var kind = AppointmentKinds.All.Contains(appointment.Kind)
            ? appointment.Kind.Replace('_', ' ')
            : "appointment";

        var when = appointment.StartsAt.ToString("dddd d MMMM yyyy 'at' HH:mm");
        var subject = what switch
        {
            "cancelled" => "Your appointment has been cancelled",
            "moved" => "Your appointment has been moved",
            _ => "Your appointment is confirmed",
        };

        var body = what == "cancelled"
            ? $"<p>Your {kind} on {when} has been cancelled.</p>" +
              "<p>Call us or book again online whenever suits you.</p>"
            : $"<p>Your {kind} is booked for <strong>{when}</strong>.</p>" +
              "<p>Please bring your current glasses and any prescription you already have.</p>";

        await email.QueueAsync("appointment", patient.Email!, $"{patient.FirstName} {patient.LastName}".Trim(), subject, body,
            "Appointment", appointment.Id, ct);
    }
}

