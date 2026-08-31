using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Text;
using VisionCart.Application.Common;
using VisionCart.Application.Email;
using VisionCart.Application.Patients;
using VisionCart.Application.Platform;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;

namespace VisionCart.Web.Controllers;

public sealed class LoginInput
{
    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; } = true;
    public string? ReturnUrl { get; set; }
}

public sealed class RegisterInput
{
    [Required(ErrorMessage = "Enter your name.")]
    [StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Phone] [StringLength(30)] public string? Phone { get; set; }

    [Required(ErrorMessage = "Choose a password.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Use at least 8 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The two passwords don't match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

public sealed class ForgotPasswordInput
{
    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordInput
{
    [Required] public string Email { get; set; } = string.Empty;
    [Required] public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a new password.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Use at least 8 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The two passwords don't match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

/// <summary>
/// Sign-in, registration and password recovery.
///
/// Replaces the legacy jose/bcryptjs implementation with ASP.NET Core Identity,
/// which brings three things the legacy system did not have: lockout after
/// repeated failures, server-side session revocation via the security stamp, and
/// single-use expiring tokens — the mechanism the new password reset is built on.
/// </summary>
public class AccountController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IPatientService patients,
    IEmailService email,
    IAuditService audit,
    IApplicationDbContext db,
    ILogger<AccountController> logger) : Controller
{
    // --- Sign in ------------------------------------------------------------

    [HttpGet("/login")]
    public IActionResult Login([FromQuery] string? next)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToLocal(next);
        return View(new LoginInput { ReturnUrl = next });
    }

    [HttpPost("/login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromForm] LoginInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(input);

        var email = input.Email.Trim().ToLowerInvariant();
        var user = await userManager.FindByEmailAsync(email);

        // The same message for "no such account" and "wrong password", so the
        // form cannot be used to discover which addresses have accounts. The
        // legacy implementation made the same choice.
        const string generic = "Email or password is incorrect.";

        if (user is null)
        {
            ModelState.AddModelError(string.Empty, generic);
            await audit.WriteAsync(AuditActions.AuthLoginFailed, "User", null, new { email }, ct);
            return View(input);
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "This account has been disabled.");
            return View(input);
        }

        var result = await signInManager.PasswordSignInAsync(
            user, input.Password, input.RememberMe, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty,
                "Too many failed attempts. Please try again in a few minutes.");
            await audit.WriteAsync(AuditActions.AuthLoginFailed, "User", user.Id,
                new { reason = "lockout" }, ct);
            return View(input);
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, generic);
            await audit.WriteAsync(AuditActions.AuthLoginFailed, "User", user.Id, null, ct);
            return View(input);
        }

        var tracked = await db.Users.FirstAsync(u => u.Id == user.Id, ct);
        tracked.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.AuthLogin, "User", user.Id, new { role = user.Role }, ct);

        // Staff land in the back office; customers land where they were going.
        if (string.IsNullOrEmpty(input.ReturnUrl) && Roles.StaffRoles.Contains(user.Role))
            return Redirect("/admin");

        return RedirectToLocal(input.ReturnUrl);
    }

    /// <summary>
    /// Signing out changes state, so it is a POST with a token — a GET would let
    /// any page on the internet sign our customers out with an image tag.
    /// </summary>
    [HttpPost("/logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return Redirect("/");
    }

    /// <summary>
    /// Somebody typed /logout into the address bar. That is a GET, so it must not
    /// sign anyone out; sending them to the button that does is friendlier than
    /// the 405 they got before.
    /// </summary>
    [HttpGet("/logout")]
    public IActionResult LogoutPrompt() =>
        User.Identity?.IsAuthenticated == true ? Redirect("/account") : Redirect("/");

    // --- Register -----------------------------------------------------------

    [HttpGet("/register")]
    public IActionResult Register([FromQuery] string? next)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToLocal(next);
        return View(new RegisterInput { ReturnUrl = next });
    }

    [HttpPost("/register")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register([FromForm] RegisterInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(input);

        var email = input.Email.Trim().ToLowerInvariant();

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            ModelState.AddModelError(nameof(input.Email),
                "An account with that email already exists.");
            return View(input);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            PhoneNumber = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim(),
            Name = input.Name.Trim(),
            Role = Roles.Customer,
            IsActive = true,
        };

        var created = await userManager.CreateAsync(user, input.Password);

        if (!created.Succeeded)
        {
            foreach (var error in created.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(input);
        }

        await userManager.AddToRoleAsync(user, Roles.Customer);

        // Every customer gets a patient file from day one — the shop is an
        // optical practice, so the clinical record is the primary entity.
        await patients.EnsureForUserAsync(user.Id, ct);

        await signInManager.SignInAsync(user, isPersistent: true);
        await audit.WriteAsync(AuditActions.PatientCreate, "User", user.Id, null, ct);

        return RedirectToLocal(input.ReturnUrl);
    }

    // --- Password recovery --------------------------------------------------

    [HttpGet("/forgot-password")]
    public IActionResult ForgotPassword() => View(new ForgotPasswordInput());

    [HttpPost("/forgot-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(
        [FromForm] ForgotPasswordInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(input);

        var email = input.Email.Trim().ToLowerInvariant();
        var user = await userManager.FindByEmailAsync(email);

        // Always the same answer, whether or not the address exists. Telling the
        // caller "no such account" would turn this form into an account
        // enumeration oracle.
        if (user is not null && user.IsActive)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            var resetUrl = Url.Action(nameof(ResetPassword), "Account",
                new { email = user.Email, token = encoded }, Request.Scheme)!;

            await email_QueueAsync(user, resetUrl, ct);
            await audit.WriteAsync(AuditActions.AuthPasswordReset, "User", user.Id,
                new { requested = true }, ct);
        }
        else
        {
            logger.LogInformation("Password reset requested for unknown address");
        }

        return View("ForgotPasswordSent");
    }

    private Task email_QueueAsync(ApplicationUser user, string resetUrl, CancellationToken ct) =>
        email.QueuePasswordResetAsync(user.Email!, user.Name, resetUrl, ct);

    [HttpGet("/reset-password")]
    public IActionResult ResetPassword([FromQuery] string? email, [FromQuery] string? token)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            return View("ResetPasswordInvalid");

        return View(new ResetPasswordInput { Email = email, Token = token });
    }

    [HttpPost("/reset-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(
        [FromForm] ResetPasswordInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(input);

        var user = await userManager.FindByEmailAsync(input.Email.Trim().ToLowerInvariant());

        // Same non-committal response as above for an unknown address.
        if (user is null) return View("ResetPasswordDone");

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(input.Token));
        }
        catch (FormatException)
        {
            return View("ResetPasswordInvalid");
        }

        var result = await userManager.ResetPasswordAsync(user, token, input.Password);

        if (!result.Succeeded)
        {
            // An expired or already-used token lands here. Identity's tokens are
            // single-use by design, which is the property that makes this safe.
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty,
                    error.Code is "InvalidToken"
                        ? "That reset link has expired or has already been used. Please request a new one."
                        : error.Description);
            }
            return View(input);
        }

        // Rotating the security stamp signs out every session already issued —
        // the point of a reset when an account may be compromised.
        await userManager.UpdateSecurityStampAsync(user);
        await audit.WriteAsync(AuditActions.AuthPasswordReset, "User", user.Id,
            new { completed = true }, ct);

        return View("ResetPasswordDone");
    }

    // --- Customer account ---------------------------------------------------

    [Authorize]
    [HttpGet("/account")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = userManager.GetUserId(User)!;

        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.PlacedAt)
            .Take(10)
            .ToListAsync(ct);

        return View(orders);
    }

    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : Redirect("/");
}
