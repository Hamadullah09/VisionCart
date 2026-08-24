using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Appointments;
using VisionCart.Application.Privacy;
using VisionCart.Domain.Constants;
using VisionCart.Infrastructure.Persistence;
using VisionCart.Web.Areas.Admin.Models;

namespace VisionCart.Web.Areas.Admin.Controllers;

/// <summary>The clinic diary: who is coming in, and when.</summary>
[Area("Admin")]
[Route("admin/diary")]
public class DiaryController(
    IAppointmentService appointments,
    ApplicationDbContext db) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] DateOnly? from, [FromQuery] int days, CancellationToken ct)
    {
        var start = from ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var span = days is >= 1 and <= 31 ? days : 7;

        var clinicians = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && (u.Role == Roles.Optician || u.Role == Roles.Admin))
            .OrderBy(u => u.Name)
            .ToListAsync(ct);

        return View(new DiaryViewModel
        {
            From = start,
            DayCount = span,
            Days = await appointments.DiaryAsync(start, span, ct),
            Clinicians = clinicians,
        });
    }

    [HttpPost("{id}/status")]
    public async Task<IActionResult> SetStatus(
        string id, string status, DateOnly? from, CancellationToken ct) =>
        Back(await appointments.SetStatusAsync(id, status, ct), nameof(Index), new { from });

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(
        string id, string? reason, DateOnly? from, CancellationToken ct) =>
        Back(await appointments.CancelAsync(id, reason, ct), nameof(Index), new { from });

    [HttpPost("{id}/reschedule")]
    public async Task<IActionResult> Reschedule(
        string id, DateTime startsAt, DateOnly? from, CancellationToken ct) =>
        Back(await appointments.RescheduleAsync(id, startsAt, ct), nameof(Index), new { from });

    [HttpPost("book")]
    public async Task<IActionResult> Book(
        string patientId, DateTime startsAt, string kind, int minutes,
        string? staffUserId, string? notes, DateOnly? from, CancellationToken ct)
    {
        var result = await appointments.BookAsync(new BookingInput
        {
            PatientId = patientId,
            StartsAt = startsAt,
            Kind = kind,
            Minutes = minutes <= 0 ? 30 : minutes,
            StaffUserId = staffUserId,
            Notes = notes,
        }, ct);

        TempData[result.Ok ? "AdminOk" : "AdminError"] =
            result.Ok ? "Appointment booked." : result.Error;

        return RedirectToAction(nameof(Index), new { from });
    }
}

/// <summary>
/// The data-subject request queue.
///
/// Reading and progressing a request is staff work. **Erasure is not** — it is
/// irreversible and it rewrites a clinical record, so it is restricted to an
/// administrator.
/// </summary>
[Area("Admin")]
[Route("admin/data-requests")]
public class DataRequestsController(
    IDataSubjectService requests,
    ApplicationDbContext db) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? status, [FromQuery] int page, CancellationToken ct) =>
        View(new DataRequestsViewModel
        {
            Requests = await requests.QueueAsync(status, page < 1 ? 1 : page, ct),
            Status = status,
        });

    [HttpGet("{id}")]
    public async Task<IActionResult> Detail(string id, CancellationToken ct)
    {
        var request = await requests.FindAsync(id, ct);
        if (request is null) return NotFound();

        var patient = request.PatientId is null ? null
            : await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PatientId, ct);

        return View(new DataRequestDetailViewModel
        {
            Request = request,
            Patient = patient,
            Impact = request.PatientId is null ? null
                : await requests.AssessErasureAsync(request.PatientId, ct),
        });
    }

    [HttpPost("{id}/status")]
    public async Task<IActionResult> SetStatus(
        string id, string status, string? staffNotes, CancellationToken ct) =>
        Back(await requests.SetStatusAsync(id, status, staffNotes, ct), nameof(Detail), new { id });

    /// <summary>
    /// Actions an erasure. Administrator only, and deliberately separate from the
    /// status form so it cannot be triggered by picking a dropdown value.
    /// </summary>
    [HttpPost("{id}/erase")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Erase(string id, string confirm, CancellationToken ct)
    {
        if (!string.Equals(confirm, "ERASE", StringComparison.Ordinal))
        {
            TempData["AdminError"] = "Type ERASE to confirm. Nothing has been changed.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        var result = await requests.EraseAsync(id, ct);
        TempData[result.Ok ? "AdminOk" : "AdminError"] =
            result.Ok ? "The customer's identifying details have been erased." : result.Error;

        return RedirectToAction(nameof(Detail), new { id });
    }
}
