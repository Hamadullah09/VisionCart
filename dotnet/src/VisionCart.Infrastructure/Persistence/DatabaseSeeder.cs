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
        var vendors = await SeedVendorsAsync(ct);
        await SeedCatalogueAsync(brands, vendors, categories, webRootPath, ct);
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
    /// The distributors the practice buys from.
    ///
    /// Distinct from brands: a brand is what a customer reads on the arm, a
    /// vendor is who an order goes to. One vendor carries several brands, and
    /// a brand often arrives through more than one of them.
    /// </summary>
    private async Task<List<Vendor>> SeedVendorsAsync(CancellationToken ct)
    {
        var seed = new (string Name, string Code, string Contact, string Phone, int Lead)[]
        {
            ("Opticore Distribution", "opticore", "Sales desk", "+92 21 0000001", 7),
            ("Northgate Optical", "northgate", "Account manager", "+92 42 0000002", 10),
            ("Lumen Eyewear Supply", "lumen", "Trade counter", "+92 21 0000003", 5),
            ("Karachi Optical House", "koh", "Wholesale", "+92 21 0000004", 3),
        };

        foreach (var v in seed)
        {
            if (await db.Vendors.AnyAsync(x => x.Code == v.Code, ct)) continue;

            db.Vendors.Add(new Vendor
            {
                Name = v.Name,
                Code = v.Code,
                ContactName = v.Contact,
                Email = "orders@example.com",
                Phone = v.Phone,
                LeadTimeDays = v.Lead,
                IsActive = true,
            });
        }

        await db.SaveChangesAsync(ct);
        return await db.Vendors.ToListAsync(ct);
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
        string[] Categories, bool Featured, string Description,
        string[]? Features = null, string PromotionGrade = "B", string Vendor = "opticore");

    private static readonly FrameModel[] Models =
    [
        // --- the original line ----------------------------------------------
        new("Ravi", "rectangle", "meridian", 6500, null, "acetate", "unisex", "round,oval,heart", 24,
            ["eyeglasses"], true, "A straightforward rectangle that suits almost everyone. Deep enough for progressives.",
            [FrameFeatures.IntegratedNosePads, FrameFeatures.SpringHinges], "A", "opticore"),
        new("Noor", "round", "aster", 8900, 10500, "titanium", "unisex", "square,oblong,diamond", 16,
            ["eyeglasses"], true, "Light enough to forget you are wearing them. Softens a strong jaw.",
            [FrameFeatures.AdjustableNosePads, FrameFeatures.Lightweight, FrameFeatures.Hypoallergenic], "A", "northgate"),
        new("Zara", "cat_eye", "juno", 7400, null, "acetate", "women", "round,square,oval", 22,
            ["eyeglasses"], true, "An upswept corner that lifts the whole face. Not shy.",
            [FrameFeatures.IntegratedNosePads], "A", "lumen"),
        new("Falcon", "aviator", "kestrel", 9800, 12000, "metal", "men", "square,oval,heart", 28,
            ["sunglasses"], true, "The teardrop, done properly. Comes tinted; add polarisation for driving.",
            [FrameFeatures.AdjustableNosePads, FrameFeatures.AdjustableTemples], "A", "northgate"),
        new("Harbour", "wayfarer", "kestrel", 7200, null, "acetate", "unisex", "round,oval,diamond", 26,
            ["eyeglasses", "sunglasses"], false, "Thick acetate with a wide brow \u2014 the frame everyone recognises.",
            [FrameFeatures.IntegratedNosePads], "B", "lumen"),
        new("Atlas", "square", "meridian", 6900, null, "tr90", "men", "round,oval", 20,
            ["eyeglasses", "blue-light"], false, "Bigger, flexible and hard to break. Good for screen days.",
            [FrameFeatures.Flexible, FrameFeatures.Lightweight, FrameFeatures.SpringHinges], "B", "opticore"),
        new("Lyra", "oval", "aster", 8200, null, "stainless", "women", "square,oblong,heart", 15,
            ["eyeglasses", "reading"], false, "Semi-rimless and barely there. Reads as jewellery more than eyewear.",
            [FrameFeatures.AdjustableNosePads, FrameFeatures.Lightweight], "B", "northgate"),
        new("Vector", "geometric", "kestrel", 8600, null, "metal", "unisex", "round,oval", 19,
            ["eyeglasses"], false, "A hexagon that stops just short of being a costume.",
            [FrameFeatures.AdjustableNosePads], "C", "lumen"),
        new("Clark", "browline", "meridian", 7800, null, "mixed", "men", "oval,round,diamond", 23,
            ["eyeglasses"], false, "Heavy brow, light rim. Structure without weight.",
            [FrameFeatures.AdjustableNosePads, FrameFeatures.SpringHinges], "B", "opticore"),
        new("Wren", "rectangle", "juno", 9600, null, "titanium", "unisex", "round,heart,diamond", 12,
            ["eyeglasses"], false, "Rimless and 12 grams. Nothing between you and the world.",
            [FrameFeatures.AdjustableNosePads, FrameFeatures.Lightweight, FrameFeatures.Hypoallergenic], "B", "northgate"),

        // --- children ---------------------------------------------------------
        // Genuinely smaller frames rather than shrunken adult ones: a 44 mm lens,
        // a short arm, and the features that survive a school bag.
        new("Pip", "rectangle", "juno", 3500, null, "tr90", "kids", "round,oval", 14,
            ["kids", "eyeglasses"], true, "Bends nearly in half and springs back. For the child who sits on them.",
            [FrameFeatures.Flexible, FrameFeatures.SpringHinges, FrameFeatures.Lightweight], "A", "koh"),
        new("Bramble", "round", "juno", 3800, null, "acetate", "kids", "square,heart", 16,
            ["kids", "eyeglasses"], false, "A proper round lens in a small size. Deep enough to grow into.",
            [FrameFeatures.IntegratedNosePads, FrameFeatures.SpringHinges], "B", "koh"),
        new("Otter", "square", "meridian", 4200, null, "tr90", "kids", "round,oval", 15,
            ["kids", "eyeglasses", "blue-light"], false, "Squared off and unfussy. Survives being cleaned on a jumper.",
            [FrameFeatures.Flexible, FrameFeatures.SpringHinges, FrameFeatures.LowBridgeFit], "B", "koh"),
        new("Finch", "oval", "juno", 3400, null, "stainless", "kids", "square,oblong", 11,
            ["kids", "eyeglasses"], false, "The smallest frame we fit, and the lightest. For a first pair.",
            [FrameFeatures.AdjustableNosePads, FrameFeatures.Lightweight, FrameFeatures.Hypoallergenic], "C", "koh"),
        new("Cobble", "wayfarer", "kestrel", 4600, null, "acetate", "kids", "round,oval,heart", 18,
            ["kids", "eyeglasses"], false, "A grown-up shape, made small. Teenagers stop arguing about these.",
            [FrameFeatures.IntegratedNosePads, FrameFeatures.SpringHinges], "B", "koh"),

        // --- women -------------------------------------------------------------
        new("Marisa", "cat_eye", "juno", 7900, 9200, "acetate", "women", "round,square,oval", 21,
            ["eyeglasses"], true, "A softer cat eye than most \u2014 the lift without the drama.",
            [FrameFeatures.IntegratedNosePads, FrameFeatures.SpringHinges], "A", "lumen"),
        new("Saffron", "round", "aster", 9400, null, "titanium", "women", "square,oblong,diamond", 14,
            ["eyeglasses"], false, "Semi-rimless and round, in brushed titanium. Almost nothing on the face.",
            [FrameFeatures.AdjustableNosePads, FrameFeatures.Lightweight, FrameFeatures.Hypoallergenic], "B", "northgate"),
        new("Indigo", "geometric", "meridian", 8100, null, "acetate", "women", "round,oval,heart", 20,
            ["eyeglasses", "blue-light"], false, "Angular without being severe. Deep enough for progressives.",
            [FrameFeatures.IntegratedNosePads], "B", "opticore"),
        new("Vela", "oval", "aster", 8800, null, "stainless", "women", "square,oblong,heart", 13,
            ["eyeglasses", "reading"], false, "A shallow oval that stays out of the way of your eyebrows.",
            [FrameFeatures.AdjustableNosePads, FrameFeatures.Lightweight], "C", "northgate"),

        // --- men ----------------------------------------------------------------
        new("Ridge", "square", "kestrel", 8400, null, "acetate", "men", "round,oval", 29,
            ["eyeglasses"], true, "A big, square frame for a wide face. Nothing apologetic about it.",
            [FrameFeatures.IntegratedNosePads, FrameFeatures.SpringHinges], "A", "lumen"),
        new("Fell", "rectangle", "aster", 10200, null, "titanium", "men", "round,oval,diamond", 15,
            ["eyeglasses"], false, "Semi-rimless titanium. The frame you forget you put on.",
            [FrameFeatures.AdjustableNosePads, FrameFeatures.Lightweight, FrameFeatures.Hypoallergenic], "B", "northgate"),
        new("Barlow", "browline", "meridian", 8300, null, "mixed", "men", "oval,round,diamond", 24,
            ["eyeglasses"], false, "A heavier brow than Clark, and a deeper lens with it.",
            [FrameFeatures.AdjustableNosePads, FrameFeatures.SpringHinges], "B", "opticore"),
        new("Tarn", "round", "kestrel", 7600, null, "metal", "men", "square,oblong", 18,
            ["eyeglasses"], false, "A wide bridge and a full round lens. Built for a low nose bridge.",
            [FrameFeatures.AdjustableNosePads, FrameFeatures.LowBridgeFit], "C", "lumen"),

        // --- sunglasses ----------------------------------------------------------
        new("Dune", "wayfarer", "kestrel", 9100, 11000, "acetate", "unisex", "round,oval,heart", 27,
            ["sunglasses"], true, "A wide tinted wayfarer. Add polarisation if you drive into the sun.",
            [FrameFeatures.IntegratedNosePads], "A", "lumen"),
        new("Solstice", "oval", "meridian", 8700, null, "metal", "unisex", "square,oblong", 22,
            ["sunglasses"], false, "A long oval lens that covers properly at the edges.",
            [FrameFeatures.AdjustableNosePads, FrameFeatures.AdjustableTemples], "B", "opticore"),
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
        List<Brand> brands, List<Vendor> vendors, List<Category> categories,
        string webRootPath, CancellationToken ct)
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
        var vendorByCode = vendors
            .Where(v => v.Code is not null)
            .ToDictionary(v => v.Code!, v => v.Id, StringComparer.Ordinal);
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
            frame.Features = model.Features is { Length: > 0 } feat ? string.Join(",", feat) : null;
            frame.PromotionGrade = model.PromotionGrade;
            frame.VendorId = vendorByCode.GetValueOrDefault(model.Vendor);

            // A plausible trade price, and the vendor's own code for the frame.
            // Both are what a reorder starts from.
            frame.VendorProductCode = $"{model.Vendor.ToUpperInvariant()[..3]}-{model.Name.ToUpperInvariant()}";
            frame.LastCostMinor = R((int)Math.Round(model.Price * 0.4));
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
                // A spread a shop would actually have: mostly healthy, some
                // running low, a few sold out. The old formula started every
                // frame at exactly the low-stock threshold, which made the
                // "running low" filter match all of them and prove nothing.
                // Never overwrites a count somebody is relying on.
                // A frame counts as out of stock only when every colourway is
                // gone, so a spread that never empties one entirely leaves the
                // "out of stock" filter permanently showing nothing. Every
                // eighth frame is therefore fully out, as a discontinued or
                // awaited line would be.
                variant.StockQty = variant.StockQty > 0
                    ? variant.StockQty
                    : i % 8 == 3 ? 0 : 1 + (i * 5 + j * 3) % 16;

                // Somewhere to actually find it. Derived from the frame's own
                // position so a re-seed puts everything back where it was.
                variant.Aisle ??= (1 + i % 12).ToString();
                variant.Shelf ??= ((char)('A' + i % 6)).ToString();
                variant.ShelfRow ??= (1 + j % 4).ToString();
                variant.Bin ??= (1 + (i * 3 + j) % 20).ToString();
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
