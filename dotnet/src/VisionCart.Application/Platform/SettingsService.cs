using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Common;
using VisionCart.Domain.Entities;

namespace VisionCart.Application.Platform;

/// <summary>
/// Port of <c>src/lib/settings.ts</c>.
///
/// Store configuration the owner can change without a deploy. Application
/// configuration stays for secrets and infrastructure; anything a shop manager
/// should own lives here, in the database.
/// </summary>
public static class SettingKeys
{
    public const string StoreName = "store.name";
    public const string StoreTagline = "store.tagline";
    public const string StoreEmail = "store.email";
    public const string StorePhone = "store.phone";
    public const string StoreAddress = "store.address";
    public const string FreeShippingOverMinor = "store.freeShippingOverMinor";
    public const string ReturnDays = "store.returnDays";
    public const string TryOnEnabled = "tryon.enabled";
    public const string TryOnCameraEnabled = "tryon.cameraEnabled";
    public const string TryOnStoreCustomerPhotos = "tryon.storeCustomerPhotos";
    public const string CheckoutRequirePrescription = "checkout.requirePrescription";
    public const string CheckoutGuestAllowed = "checkout.guestAllowed";

    public static readonly IReadOnlyDictionary<string, string> Defaults =
        new Dictionary<string, string>
        {
            [StoreName] = "VisionCart Optical",
            [StoreTagline] = "Prescription eyewear, fitted properly.",
            [StoreEmail] = "hello@example.com",
            [StorePhone] = "+92 300 0000000",
            [StoreAddress] = "123 Main Boulevard, Lahore",
            [FreeShippingOverMinor] = "1500000",
            [ReturnDays] = "14",
            [TryOnEnabled] = "true",
            [TryOnCameraEnabled] = "true",
            [TryOnStoreCustomerPhotos] = "false",
            [CheckoutRequirePrescription] = "false",
            [CheckoutGuestAllowed] = "true",
        };
}

public interface ISettingsService
{
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default);
    Task<string> GetAsync(string key, CancellationToken ct = default);
    Task<bool> GetBoolAsync(string key, CancellationToken ct = default);
    Task<int> GetIntAsync(string key, int fallback = 0, CancellationToken ct = default);
    Task SetAsync(string key, string value, string group = "general", CancellationToken ct = default);
    void Invalidate();
}

public sealed class SettingsService(IApplicationDbContext db) : ISettingsService
{
    // Settings are read on nearly every request (banner, try-on switches, guest
    // checkout) and written rarely. A short cache keeps a dozen queries off the
    // hot path; every write invalidates it.
    private static IReadOnlyDictionary<string, string>? _cache;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        if (_cache is not null) return _cache;

        await Gate.WaitAsync(ct);
        try
        {
            if (_cache is not null) return _cache;

            var rows = await db.Settings.AsNoTracking().ToListAsync(ct);
            var map = new Dictionary<string, string>(SettingKeys.Defaults);
            foreach (var row in rows) map[row.Key] = row.Value;
            _cache = map;
            return map;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<string> GetAsync(string key, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.TryGetValue(key, out var value) ? value : string.Empty;
    }

    public async Task<bool> GetBoolAsync(string key, CancellationToken ct = default) =>
        string.Equals(await GetAsync(key, ct), "true", StringComparison.OrdinalIgnoreCase);

    public async Task<int> GetIntAsync(string key, int fallback = 0, CancellationToken ct = default) =>
        int.TryParse(await GetAsync(key, ct), out var n) ? n : fallback;

    public async Task SetAsync(string key, string value, string group = "general", CancellationToken ct = default)
    {
        var existing = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (existing is null)
        {
            db.Settings.Add(new Setting { Key = key, Value = value, Group = group });
        }
        else
        {
            existing.Value = value;
            existing.Group = group;
        }

        await db.SaveChangesAsync(ct);
        Invalidate();
    }

    public void Invalidate() => _cache = null;
}
