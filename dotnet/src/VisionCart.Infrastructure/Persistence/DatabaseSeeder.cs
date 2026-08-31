using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VisionCart.Application.Platform;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;

namespace VisionCart.Infrastructure.Persistence;

/// <summary>
/// Port of <c>prisma/seed.ts</c>. Idempotent: safe to run on every start, so a
/// fresh deployment comes up with a working shop and an existing one is
/// untouched.
///
/// The staff passwords come from configuration, never from a literal here — the
/// legacy seed's published demo passwords must not reach production.
/// </summary>
public sealed class DatabaseSeeder(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    RoleManager<ApplicationRole> roles,
    ILogger<DatabaseSeeder> logger)
{
    /// <summary>Major units to minor. Mirrors the seed script's <c>R()</c> helper.</summary>
    private static int R(int major) => major * 100;

    public async Task SeedAsync(SeedOptions options, string webRootPath, CancellationToken ct = default)
    {
        await SeedRolesAsync();
        await SeedStaffAsync(options);
        await SeedLensOptionsAsync(ct);
        var categories = await SeedCategoriesAsync(ct);
        var brands = await SeedBrandsAsync(ct);
        await SeedCatalogueAsync(brands, categories, webRootPath, ct);
        await SeedShippingAsync(ct);
        await SeedPromotionsAsync(ct);
        await SeedSettingsAsync(ct);

        logger.LogInformation("Database seed complete");
    }

    private async Task SeedRolesAsync()
    {
        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new ApplicationRole(role));
        }
    }

    private async Task SeedStaffAsync(SeedOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AdminEmail) || string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            logger.LogWarning(
                "No seed administrator configured; skipping staff seed. " +
                "Set Seed:AdminEmail and Seed:AdminPassword to create the first account.");
            return;
        }

        await EnsureUserAsync(options.AdminEmail, options.AdminPassword, "Store Owner", Roles.Admin);

        if (!string.IsNullOrWhiteSpace(options.OpticianEmail) &&
            !string.IsNullOrWhiteSpace(options.OpticianPassword))
        {
            await EnsureUserAsync(options.OpticianEmail, options.OpticianPassword,
                "Duty Optician", Roles.Optician);
        }
    }

    private async Task EnsureUserAsync(string email, string password, string name, string role)
    {
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (!await users.IsInRoleAsync(existing, role)) await users.AddToRoleAsync(existing, role);
            return;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            Name = name,
            Role = role,
            IsActive = true,
        };

        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogError("Could not create seed user {Email}: {Errors}",
                email, string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await users.AddToRoleAsync(user, role);
        logger.LogInformation("Created seed {Role} account {Email}", role, email);
    }

    private async Task SeedLensOptionsAsync(CancellationToken ct)
    {
        var options = new (string Group, string Code, string Name, int Price, bool IsDefault, int Position,
            double? MaxSphere, string? Description, string? Excludes)[]
        {
            (LensGroups.Usage, "use-everyday", "Everyday distance", 0, true, 0, null, "Driving, TV, general wear.", null),
            (LensGroups.Usage, "use-reading", "Reading & close work", 0, false, 1, null, "Books, phone, handwork.", null),
            (LensGroups.Usage, "use-screen", "Screens & office", 0, false, 2, null, "Tuned for a monitor at arm's length.", null),

            (LensGroups.Type, "type-single", "Single vision", 0, true, 0, null, "One prescription across the whole lens.", null),
            (LensGroups.Type, "type-bifocal", "Bifocal", R(3500), false, 1, null, "Distance on top, reading below, with a visible line.", null),
            (LensGroups.Type, "type-progressive", "Progressive", R(9500), false, 2, null, "Distance to reading with no line.", null),
            (LensGroups.Type, "type-office", "Office / desk progressive", R(7500), false, 3, null, "Optimised for screen and desk distance.", null),

            (LensGroups.Index, "idx-150", "1.50 standard", 0, true, 0, 3, "Fine for light prescriptions.", null),
            (LensGroups.Index, "idx-161", "1.61 thin", R(2500), false, 1, 6, "About 20% thinner and lighter.", null),
            (LensGroups.Index, "idx-167", "1.67 extra thin", R(5500), false, 2, 9, "For stronger prescriptions.", null),
            (LensGroups.Index, "idx-174", "1.74 ultra thin", R(11000), false, 3, 20, "The thinnest we make.", null),

            (LensGroups.Coating, "coat-hard", "Scratch-resistant hard coat", 0, true, 0, null, "Included on every lens.", null),
            (LensGroups.Coating, "coat-ar", "Anti-reflective", R(1800), true, 1, null, "Cuts glare from headlights and screens.", null),
            (LensGroups.Coating, "coat-blue", "Blue-light filter", R(2400), false, 2, null, "Takes the edge off long screen days.", null),
            (LensGroups.Coating, "coat-uv", "UV400 protection", R(900), false, 3, null, "Blocks UV up to 400nm.", null),
            (LensGroups.Coating, "coat-oleo", "Water & smudge repellent", R(1200), false, 4, null, null, null),

            (LensGroups.Tint, "tint-none", "Clear", 0, true, 0, null, null, null),
            (LensGroups.Tint, "tint-grey", "Solid grey", R(2000), false, 1, null, "True-to-life colour in bright sun.", null),
            (LensGroups.Tint, "tint-brown", "Solid brown", R(2000), false, 2, null, "Warmer, boosts contrast.", null),
            (LensGroups.Tint, "tint-photo", "Photochromic", R(6500), false, 3, null, "Clear indoors, dark in sunlight.", null),
            (LensGroups.Tint, "tint-polar", "Polarised", R(7500), false, 4, null, "Kills glare off water and roads.", "tint-photo"),

            (LensGroups.Extra, "extra-case", "Hard case & cloth", 0, true, 0, null, null, null),
            (LensGroups.Extra, "extra-warranty", "2-year breakage cover", R(2500), false, 1, null, "One free replacement if they break.", null),
            (LensGroups.Extra, "extra-thin-edge", "Edge polish & bevel", R(1500), false, 2, null, "Tidier edges on stronger prescriptions.", null),
        };

        foreach (var o in options)
        {
            var existing = await db.LensOptions.FirstOrDefaultAsync(l => l.Code == o.Code, ct);
            if (existing is null)
            {
                db.LensOptions.Add(new LensOption
                {
                    Group = o.Group, Code = o.Code, Name = o.Name, PriceMinor = o.Price,
                    IsDefault = o.IsDefault, Position = o.Position, MaxSphere = o.MaxSphere,
                    Description = o.Description, Excludes = o.Excludes, IsActive = true,
                });
            }
            else
            {
                existing.Name = o.Name;
                existing.PriceMinor = o.Price;
                existing.IsDefault = o.IsDefault;
                existing.Position = o.Position;
                existing.MaxSphere = o.MaxSphere;
                existing.Description = o.Description;
                existing.Excludes = o.Excludes;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<List<Category>> SeedCategoriesAsync(CancellationToken ct)
    {
        var categories = new (string Name, string Slug, int Position)[]
        {
            ("Eyeglasses", "eyeglasses", 0),
            ("Sunglasses", "sunglasses", 1),
            ("Blue-light glasses", "blue-light", 2),
            ("Reading glasses", "reading", 3),
            ("Kids", "kids", 4),
        };

        foreach (var c in categories)
        {
            if (!await db.Categories.AnyAsync(x => x.Slug == c.Slug, ct))
                db.Categories.Add(new Category { Name = c.Name, Slug = c.Slug, Position = c.Position });
        }

        await db.SaveChangesAsync(ct);
        return await db.Categories.ToListAsync(ct);
    }

    private async Task<List<Brand>> SeedBrandsAsync(CancellationToken ct)
    {
        var brands = new (string Name, string Slug, string About)[]
        {
            ("Meridian", "meridian", "Our own line — classic shapes, honest prices."),
            ("Aster", "aster", "Light titanium frames for all-day wear."),
            ("Kestrel", "kestrel", "Bold acetate with a bit of attitude."),
            ("Juno", "juno", "Softer shapes designed for smaller faces."),
        };

        foreach (var b in brands)
        {
            if (!await db.Brands.AnyAsync(x => x.Slug == b.Slug, ct))
                db.Brands.Add(new Brand { Name = b.Name, Slug = b.Slug, About = b.About });
        }

        await db.SaveChangesAsync(ct);
        return await db.Brands.ToListAsync(ct);
    }

    /// <summary>
    /// Deterministic per-model commercial details so re-seeding doesn't shuffle
    /// the shop.
    ///
    /// The physical measurements are deliberately absent: they belong with the
    /// artwork, which is drawn to them, and are read back from the manifest.
    /// Holding them in two places is how a picture and a spec sheet come to
    /// describe different frames.
    /// </summary>
    private sealed record FrameModel(
        string Name, string Shape, string Brand, int Price, int? CompareAt, string Material,
        string Gender, string FaceShapes, int Weight,
        string[] Categories, bool Featured, string Description);

    private static readonly FrameModel[] Models =
    [
        new("Ravi", "rectangle", "meridian", 6500, null, "acetate", "unisex", "round,oval,heart", 24,
            ["eyeglasses"], true, "A straightforward rectangle that suits almost everyone. Deep enough for progressives."),
        new("Noor", "round", "aster", 8900, 10500, "titanium", "unisex", "square,oblong,diamond", 16,
            ["eyeglasses"], true, "Light enough to forget you're wearing them. Softens a strong jaw."),
        new("Zara", "cat_eye", "juno", 7400, null, "acetate", "women", "round,square,oval", 22,
            ["eyeglasses"], true, "An upswept corner that lifts the whole face. Not shy."),
        new("Falcon", "aviator", "kestrel", 9800, 12000, "metal", "men", "square,oval,heart", 28,
            ["sunglasses"], true, "The teardrop, done properly. Comes tinted; add polarisation for driving."),
        new("Harbour", "wayfarer", "kestrel", 7200, null, "acetate", "unisex", "round,oval,diamond", 26,
            ["eyeglasses", "sunglasses"], false, "Thick acetate with a wide brow — the frame everyone recognises."),
        new("Atlas", "square", "meridian", 6900, null, "tr90", "men", "round,oval", 20,
            ["eyeglasses", "blue-light"], false, "Bigger, flexible and hard to break. Good for screen days."),
        new("Lyra", "oval", "aster", 8200, null, "stainless", "women", "square,oblong,heart", 15,
            ["eyeglasses", "reading"], false, "Semi-rimless and barely there. Reads as jewellery more than eyewear."),
        new("Vector", "geometric", "kestrel", 8600, null, "metal", "unisex", "round,oval", 19,
            ["eyeglasses"], false, "A hexagon that stops just short of being a costume."),
        new("Clark", "browline", "meridian", 7800, null, "mixed", "men", "oval,round,diamond", 23,
            ["eyeglasses"], false, "Heavy brow, light rim. Structure without weight."),
        new("Wren", "rectangle", "juno", 9600, null, "titanium", "unisex", "round,heart,diamond", 12,
            ["eyeglasses"], false, "Rimless and 12 grams. Nothing between you and the world."),
    ];

    /// <summary>
    /// One generated artwork file, as the generator described it.
    ///
    /// It carries the millimetres it drew to and the calibration it drew them
    /// at, so the picture and the product record cannot disagree about where
    /// the lenses are.
    /// </summary>
    private sealed class FrameAsset
    {
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("frame")] public string Frame { get; set; } = string.Empty;
        [JsonPropertyName("shape")] public string Shape { get; set; } = string.Empty;
        [JsonPropertyName("rimType")] public string RimType { get; set; } = "full_rim";
        [JsonPropertyName("color")] public string Color { get; set; } = string.Empty;
        [JsonPropertyName("colorLabel")] public string ColorLabel { get; set; } = string.Empty;
        [JsonPropertyName("colorHex")] public string ColorHex { get; set; } = "#000000";
        [JsonPropertyName("tinted")] public bool Tinted { get; set; }
        [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
        [JsonPropertyName("imageWidth")] public int ImageWidth { get; set; }
        [JsonPropertyName("imageHeight")] public int ImageHeight { get; set; }
        [JsonPropertyName("lensWidthMm")] public double LensWidthMm { get; set; }
        [JsonPropertyName("bridgeWidthMm")] public double BridgeWidthMm { get; set; }
        [JsonPropertyName("templeLengthMm")] public double TempleLengthMm { get; set; }
        [JsonPropertyName("lensHeightMm")] public double LensHeightMm { get; set; }
        [JsonPropertyName("totalWidthMm")] public double TotalWidthMm { get; set; }
        [JsonPropertyName("calibration")] public FrameAssetCalibration? Calibration { get; set; }
    }

    private sealed class FrameAssetCalibration
    {
        [JsonPropertyName("leftLensCenterX")] public double LeftLensCenterX { get; set; }
        [JsonPropertyName("leftLensCenterY")] public double LeftLensCenterY { get; set; }
        [JsonPropertyName("rightLensCenterX")] public double RightLensCenterX { get; set; }
        [JsonPropertyName("rightLensCenterY")] public double RightLensCenterY { get; set; }
        [JsonPropertyName("frontLeftX")] public double FrontLeftX { get; set; }
        [JsonPropertyName("frontRightX")] public double FrontRightX { get; set; }
        [JsonPropertyName("lensTopY")] public double LensTopY { get; set; }
        [JsonPropertyName("lensBottomY")] public double LensBottomY { get; set; }
    }

    private async Task SeedCatalogueAsync(
        List<Brand> brands, List<Category> categories, string webRootPath, CancellationToken ct)
    {
        var manifestPath = Path.Combine(webRootPath, "frames", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            logger.LogWarning(
                "Frame artwork manifest not found at {Path}; skipping catalogue seed. " +
                "Run the frame generator to produce placeholder artwork.", manifestPath);
            return;
        }

        var assets = JsonSerializer.Deserialize<List<FrameAsset>>(
            await File.ReadAllTextAsync(manifestPath, ct)) ?? [];

        var brandBySlug = brands.ToDictionary(b => b.Slug, b => b.Id, StringComparer.Ordinal);
        var catBySlug = categories.ToDictionary(c => c.Slug, c => c.Id, StringComparer.Ordinal);

        for (var i = 0; i < Models.Length; i++)
        {
            var model = Models[i];
            var sku = $"VC-{model.Name.ToUpperInvariant()[..Math.Min(4, model.Name.Length)]}";

            // The generator names each asset after the frame it drew, so the
            // colourways of one frame are exactly the assets that claim it.
            var matching = assets
                .Where(a => string.Equals(a.Frame, model.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matching.Count == 0)
            {
                logger.LogWarning(
                    "No try-on artwork found for {Frame}; it will not be seeded. Run the frame generator.",
                    model.Name);
                continue;
            }

            var drawn = matching[0];

            var frame = await db.Frames.FirstOrDefaultAsync(f => f.Sku == sku, ct);
            if (frame is null)
            {
                frame = new Frame { Sku = sku, Slug = model.Name.ToLowerInvariant() };
                db.Frames.Add(frame);
            }

            frame.Name = model.Name;
            frame.BrandId = brandBySlug.GetValueOrDefault(model.Brand);
            frame.Description = model.Description;
            frame.Shape = model.Shape;
            frame.Material = model.Material;
            frame.RimType = drawn.RimType;
            frame.Gender = model.Gender;
            frame.FaceShapes = model.FaceShapes;
            // Straight from the artwork, which was drawn to these figures.
            frame.LensWidthMm = drawn.LensWidthMm;
            frame.BridgeWidthMm = drawn.BridgeWidthMm;
            frame.TempleLengthMm = drawn.TempleLengthMm;
            frame.LensHeightMm = drawn.LensHeightMm;
            frame.TotalWidthMm = drawn.TotalWidthMm;
            frame.WeightGrams = model.Weight;
            frame.SizeBand = drawn.TotalWidthMm < 130 ? SizeBands.Narrow
                           : drawn.TotalWidthMm > 143 ? SizeBands.Wide
                           : SizeBands.Medium;
            frame.BasePriceMinor = R(model.Price);
            frame.CompareAtMinor = model.CompareAt is { } c ? R(c) : null;
            frame.CostMinor = R((int)Math.Round(model.Price * 0.42));
            frame.Status = ProductStatuses.Active;
            frame.IsFeatured = model.Featured;
            frame.Position = i;
            frame.MetaTitle = $"{model.Name} — {model.Shape.Replace('_', ' ')} glasses";
            frame.MetaDesc = model.Description[..Math.Min(155, model.Description.Length)];
            frame.SearchText = BuildSearchText(model);

            await db.SaveChangesAsync(ct);

            foreach (var slug in model.Categories)
            {
                if (!catBySlug.TryGetValue(slug, out var categoryId)) continue;
                if (await db.FrameCategories.AnyAsync(
                        fc => fc.FrameId == frame.Id && fc.CategoryId == categoryId, ct)) continue;

                db.FrameCategories.Add(new FrameCategory { FrameId = frame.Id, CategoryId = categoryId });
            }

            // One colourway per matching asset, with its try-on artwork already
            // calibrated to the default anchors the generator drew it against.
            for (var j = 0; j < matching.Count; j++)
            {
                var asset = matching[j];
                var variantSku = $"{sku}-{asset.Color.ToUpperInvariant()[..Math.Min(3, asset.Color.Length)]}";

                var variant = await db.FrameVariants.FirstOrDefaultAsync(v => v.Sku == variantSku, ct);
                if (variant is null)
                {
                    variant = new FrameVariant { Sku = variantSku, FrameId = frame.Id };
                    db.FrameVariants.Add(variant);
                }

                variant.ColorName = asset.ColorLabel;
                variant.ColorHex = asset.ColorHex;
                variant.StockQty = variant.StockQty > 0 ? variant.StockQty : 3 + (j * 4) % 12;
                variant.Position = j;
                variant.IsActive = true;
                variant.TryOnImageUrl = asset.Url;
                variant.TryOnOpacity = asset.Tinted ? 0.85 : 1.0;
                variant.TryOnImageWidth = asset.ImageWidth;
                variant.TryOnImageHeight = asset.ImageHeight;

                // Exact by construction: the generator computed these from the
                // same millimetres it drew the picture at.
                if (asset.Calibration is { } cal)
                {
                    variant.AnchorLeftX = cal.LeftLensCenterX;
                    variant.AnchorLeftY = cal.LeftLensCenterY;
                    variant.AnchorRightX = cal.RightLensCenterX;
                    variant.AnchorRightY = cal.RightLensCenterY;
                    variant.TryOnFrontLeftX = cal.FrontLeftX;
                    variant.TryOnFrontRightX = cal.FrontRightX;
                    variant.TryOnLensTopY = cal.LensTopY;
                    variant.TryOnLensBottomY = cal.LensBottomY;
                }

                await db.SaveChangesAsync(ct);

                // Updated rather than only inserted: regenerated artwork changes
                // the filename, and a row left pointing at the old one is a
                // broken image on every product card in the shop.
                var primary = await db.ProductImages
                    .FirstOrDefaultAsync(p => p.VariantId == variant.Id
                                              && p.Role == ProductImageRoles.Primary, ct);

                if (primary is null)
                {
                    primary = new ProductImage
                    {
                        VariantId = variant.Id,
                        Role = ProductImageRoles.Primary,
                        Position = 0,
                    };
                    db.ProductImages.Add(primary);
                }

                primary.Url = asset.Url;
                primary.ThumbUrl = asset.Url;
                primary.Alt = $"{model.Name} in {asset.ColorLabel}";
                primary.Width = asset.ImageWidth;
                primary.Height = asset.ImageHeight;
            }

            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Denormalised search text, added during migration. One indexed predicate
    /// beats four LIKE scans joined by OR on a catalogue of any size.
    /// </summary>
    private static string BuildSearchText(FrameModel m) =>
        $"{m.Name} {m.Brand} {m.Shape} {m.Material} {m.Gender}".ToLowerInvariant();

    private async Task SeedShippingAsync(CancellationToken ct)
    {
        var rates = new (string Name, string Country, int Price, int Min, int Max, string Carrier, int Position)[]
        {
            ("Standard delivery", "PK", R(300), 3, 6, "tcs", 0),
            ("Express delivery", "PK", R(700), 1, 2, "leopards", 1),
            ("International", "AE", R(3500), 5, 10, "dhl", 0),
        };

        foreach (var r in rates)
        {
            var existing = await db.ShippingRates
                .FirstOrDefaultAsync(x => x.Name == r.Name && x.Country == r.Country, ct);

            if (existing is null)
            {
                db.ShippingRates.Add(new ShippingRate
                {
                    Name = r.Name, Country = r.Country, PriceMinor = r.Price,
                    EtaDaysMin = r.Min, EtaDaysMax = r.Max, Carrier = r.Carrier,
                    Position = r.Position, IsActive = true,
                    Code = r.Name.ToLowerInvariant().Replace(' ', '-'),
                });
            }
            else
            {
                existing.PriceMinor = r.Price;
                existing.EtaDaysMin = r.Min;
                existing.EtaDaysMax = r.Max;
                existing.Carrier = r.Carrier;
                existing.Code ??= r.Name.ToLowerInvariant().Replace(' ', '-');
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedPromotionsAsync(CancellationToken ct)
    {
        var in30Days = DateTime.UtcNow.AddDays(30);

        var promotions = new Promotion[]
        {
            new()
            {
                Name = "15% off your first pair", Code = "WELCOME15",
                Kind = PromotionKinds.PercentOff, Value = 1500,
                FirstOrderOnly = true, EndsAt = in30Days, Priority = 10, IsActive = true,
                Description = "New customers get 15% off their first order, frame and lenses.",
                BannerText = "New here? 15% off your first pair with code WELCOME15",
            },
            new()
            {
                Name = "Free delivery over Rs. 15,000", Code = null,
                Kind = PromotionKinds.FreeShipping, Value = 0,
                MinSubtotalMinor = R(15000), Stackable = true, Priority = 1, IsActive = true,
                Description = "Spend Rs. 15,000 and delivery is on us.",
            },
            new()
            {
                Name = "Two pairs, second half price", Code = "TWOPAIR",
                Kind = PromotionKinds.PercentOff, Value = 2500,
                MinQty = 2, Priority = 5, IsActive = true,
                Description = "Buy two pairs and save 25% on the whole order.",
            },
            new()
            {
                Name = "Free thin-lens upgrade", Code = "THINLENS",
                Kind = PromotionKinds.FreeLensUpgrade, Value = 0,
                MaxDiscountMinor = R(5500), EndsAt = in30Days, Priority = 3, IsActive = true,
                Description = "We'll waive the cost of a thinner lens on this order.",
            },
        };

        foreach (var p in promotions)
        {
            var existing = p.Code is null
                ? await db.Promotions.FirstOrDefaultAsync(x => x.Name == p.Name, ct)
                : await db.Promotions.FirstOrDefaultAsync(x => x.Code == p.Code, ct);

            if (existing is null) db.Promotions.Add(p);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedSettingsAsync(CancellationToken ct)
    {
        foreach (var (key, value) in SettingKeys.Defaults)
        {
            if (!await db.Settings.AnyAsync(s => s.Key == key, ct))
                db.Settings.Add(new Setting { Key = key, Value = value, Group = key.Split('.')[0] });
        }

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Seed accounts, supplied from configuration. Never defaulted to a literal:
/// the legacy seed shipped published demo passwords, and a deployment that
/// forgets to override them hands over every patient record.
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";
    public bool Enabled { get; set; } = true;
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string? OpticianEmail { get; set; }
    public string? OpticianPassword { get; set; }
}
