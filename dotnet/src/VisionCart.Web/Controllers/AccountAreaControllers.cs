using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Accounts;
using VisionCart.Application.Appointments;
using VisionCart.Application.Common;
using VisionCart.Application.Patients;
using VisionCart.Application.Privacy;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;
using VisionCart.Infrastructure.Persistence;
using VisionCart.Web.Models;

namespace VisionCart.Web.Controllers;

/// <summary>
/// The customer's address book.
///
/// Every action resolves the owner from the signed-in principal and passes it to
/// the service, which scopes its query. An address id from the form is never
/// trusted on its own.
/// </summary>
[Authorize]
[Route("account/addresses")]
public class AddressesController(
    IAddressService addresses,
    UserManager<ApplicationUser> users) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await addresses.ListAsync(users.GetUserId(User)!, ct));

    [HttpGet("new")]
    public IActionResult New() => View("Edit", new AddressInput());

    [HttpGet("{id}/edit")]
    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var address = await addresses.FindAsync(users.GetUserId(User)!, id, ct);
        if (address is null) return NotFound();

        return View(new AddressInput
        {
            Id = address.Id,
            Label = address.Label,
            FullName = address.FullName,
            Phone = address.Phone,
            Line1 = address.Line1,
            Line2 = address.Line2,
            City = address.City,
            State = address.State,
            PostalCode = address.PostalCode,
            Country = address.Country,
            IsDefault = address.IsDefault,
        });
    }

    [HttpPost("save")]
    public async Task<IActionResult> Save(AddressInput input, CancellationToken ct)
    {
        var result = await addresses.SaveAsync(users.GetUserId(User)!, input, ct);

        if (!result.Ok)
        {
            foreach (var (field, message) in result.FieldErrors) ModelState.AddModelError(field, message);
            if (result.FieldErrors.Count == 0) ModelState.AddModelError(string.Empty, result.Error!);
            return View("Edit", input);
        }

        TempData["AccountOk"] = "Address saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/delete")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var result = await addresses.DeleteAsync(users.GetUserId(User)!, id, ct);
        TempData[result.Ok ? "AccountOk" : "AccountError"] = result.Ok ? "Address removed." : result.Error;

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/default")]
    public async Task<IActionResult> MakeDefault(string id, CancellationToken ct)
    {
        var result = await addresses.MakeDefaultAsync(users.GetUserId(User)!, id, ct);
        TempData[result.Ok ? "AccountOk" : "AccountError"] =
            result.Ok ? "Default delivery address updated." : result.Error;

        return RedirectToAction(nameof(Index));
    }
}

/// <summary>Customer-facing appointment booking.</summary>
[Authorize]
[Route("account/appointments")]
public class AppointmentsController(
    IAppointmentService appointments,
    IPatientService patients,
    UserManager<ApplicationUser> users) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var patient = await patients.EnsureForUserAsync(users.GetUserId(User)!, ct);

        return View(new AppointmentsViewModel
        {
            PatientId = patient.Id,
            Appointments = await appointments.ForPatientAsync(patient.Id, ct),
        });
    }

    [HttpGet("book")]
    public async Task<IActionResult> Book(
        [FromQuery] DateOnly? date, [FromQuery] string? kind, CancellationToken ct)
    {
        var day = date ?? NextOpenDay(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));

        return View(new BookAppointmentViewModel
        {
            Date = day,
            Kind = kind is not null && AppointmentKinds.All.Contains(kind) ? kind : AppointmentKinds.EyeTest,
            Slots = await appointments.SlotsForDayAsync(day, ct: ct),
            LastBookableDate = DateOnly.FromDateTime(
                DateTime.UtcNow.AddDays(AppointmentService.BookableDaysAhead)),
        });
    }

    [HttpPost("book")]
    [EnableRateLimiting("checkout")]
    public async Task<IActionResult> Book(
        [FromForm] DateTime startsAt, [FromForm] string kind,
        [FromForm] string? notes, CancellationToken ct)
    {
        var patient = await patients.EnsureForUserAsync(users.GetUserId(User)!, ct);

        var result = await appointments.BookAsync(new BookingInput
        {
            PatientId = patient.Id,
            StartsAt = startsAt,
            Kind = kind,
            Notes = notes,
        }, ct);

        if (!result.Ok)
        {
            TempData["AccountError"] = result.Error;
            return RedirectToAction(nameof(Book),
                new { date = DateOnly.FromDateTime(startsAt), kind });
        }

        TempData["AccountOk"] = "Your appointment is booked. We have emailed you the details.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(string id, CancellationToken ct)
    {
        // Scoped to the caller's own patient file: an appointment id alone must
        // not let one customer cancel another's slot.
        var patient = await patients.EnsureForUserAsync(users.GetUserId(User)!, ct);
        var appointment = await appointments.FindAsync(id, ct);

        if (appointment is null || appointment.PatientId != patient.Id) return NotFound();

        var result = await appointments.CancelAsync(id, "Cancelled by the customer", ct);
        TempData[result.Ok ? "AccountOk" : "AccountError"] =
            result.Ok ? "Appointment cancelled." : result.Error;

        return RedirectToAction(nameof(Index));
    }

    private static DateOnly NextOpenDay(DateOnly from) =>
        from.DayOfWeek == DayOfWeek.Sunday ? from.AddDays(1) : from;
}

/// <summary>
/// The customer's own privacy controls: see the data held, ask for it to be
/// corrected, exported or erased.
/// </summary>
[Route("account/privacy")]
public class PrivacyController(
    IDataSubjectService requests,
    UserManager<ApplicationUser> users) : Controller
{
    [Authorize]
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await requests.ForUserAsync(users.GetUserId(User)!, ct));

    [Authorize]
    [HttpGet("download")]
    [EnableRateLimiting("upload")]
    public async Task<IActionResult> Download(CancellationToken ct)
    {
        var json = await requests.ExportPersonalDataAsync(users.GetUserId(User)!, ct);
        var filename = $"visioncart-my-data-{DateTime.UtcNow:yyyy-MM-dd}.json";

        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", filename);
    }

    /// <summary>
    /// Open to anyone, because the right to ask does not depend on still having a
    /// working account — someone locked out is exactly who needs it.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("request")]
    public IActionResult Raise() => View(new DataRequestInput
    {
        Email = User.Identity?.IsAuthenticated == true ? User.Identity.Name ?? string.Empty : string.Empty,
    });

    [AllowAnonymous]
    [HttpPost("request")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Raise(DataRequestInput input, CancellationToken ct)
    {
        var userId = User.Identity?.IsAuthenticated == true ? users.GetUserId(User) : null;
        var result = await requests.RaiseAsync(userId, input, ct);

        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(input);
        }

        return View("RequestSent");
    }
}
