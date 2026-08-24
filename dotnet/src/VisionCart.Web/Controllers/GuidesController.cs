using Microsoft.AspNetCore.Mvc;
using VisionCart.Application.Platform;

namespace VisionCart.Web.Controllers;

/// <summary>
/// The four customer guide pages.
///
/// These were linked from the footer of every page in the legacy application and
/// all four returned 404 — a broken link visible to every visitor. They are
/// implemented here.
///
/// Content is served from views rather than the database because it is editorial
/// copy that changes rarely; the returns window and privacy switches that *do*
/// change are read from settings so the pages cannot drift from the shop's
/// actual configuration.
/// </summary>
[Route("guides")]
public class GuidesController(ISettingsService settings) : Controller
{
    [HttpGet("prescription")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult Prescription() => View();

    [HttpGet("pd")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult Pd() => View();

    [HttpGet("returns")]
    public async Task<IActionResult> Returns(CancellationToken ct)
    {
        // Read from settings so the page can never contradict the shop's rules.
        ViewData["ReturnDays"] = await settings.GetIntAsync(SettingKeys.ReturnDays, 14, ct);
        ViewData["StoreEmail"] = await settings.GetAsync(SettingKeys.StoreEmail, ct);
        return View();
    }

    [HttpGet("privacy")]
    public async Task<IActionResult> Privacy(CancellationToken ct)
    {
        ViewData["StoresSnapshots"] = await settings.GetBoolAsync(SettingKeys.TryOnStoreCustomerPhotos, ct);
        ViewData["StoreEmail"] = await settings.GetAsync(SettingKeys.StoreEmail, ct);
        return View();
    }
}
