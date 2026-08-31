using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Common;
using VisionCart.Application.Platform;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;
using VisionCart.Domain.ValueObjects;

namespace VisionCart.Application.Admin;

public sealed class FrameDetails
{
    public string Name { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? Slug { get; set; }
    public string? BrandId { get; set; }
    public string Status { get; set; } = ProductStatuses.Draft;
    public int Position { get; set; }
    public string? Description { get; set; }
    public string? Shape { get; set; }
    public string? Material { get; set; }
    public string RimType { get; set; } = RimTypes.FullRim;
    public string Gender { get; set; } = Genders.Unisex;
    public string? FaceShapes { get; set; }
    // --- purchasing and fitting -------------------------------------------
    public string? VendorId { get; set; }
    public string? VendorProductCode { get; set; }
    public decimal? LastCost { get; set; }
    public string? PromotionGrade { get; set; }

    /// <summary>Fitting features, from the tick boxes on the frame form.</summary>
    public List<string> FeatureCodes { get; set; } = [];

    public double? LensWidthMm { get; set; }
    public double? BridgeWidthMm { get; set; }
    public double? TempleLengthMm { get; set; }
    public double? LensHeightMm { get; set; }
    public double? TotalWidthMm { get; set; }
    public double? WeightGrams { get; set; }

    /// <summary>Staff type major units; converted once, here at the edge.</summary>
    public decimal Price { get; set; }
    public decimal? CompareAt { get; set; }
    public decimal? Cost { get; set; }

    public bool AllowFrameOnly { get; set; } = true;
    public bool RequiresPrescription { get; set; }
    public bool IsFeatured { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDesc { get; set; }
    public List<string> CategoryIds { get; set; } = [];
}

public sealed class VariantDetails
{
    public string ColorName { get; set; } = string.Empty;
    public string? ColorHex { get; set; }
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public decimal? PriceOverride { get; set; }
    public int StockQty { get; set; }
    public int LowStockAt { get; set; } = 3;
    public int Position { get; set; }
    public bool IsActive { get; set; } = true;

    // Where this colourway physically sits. Free text, because practices label
    // their bays "F", "3A" and "back-left" and refusing their own labels helps
    // nobody find anything.
    public string? Aisle { get; set; }
    public string? Shelf { get; set; }
    public string? ShelfRow { get; set; }
    public string? Bin { get; set; }
}

/// <summary>
/// A vendor as the list shows it, with the number of frames bought from it.
///
/// A projection rather than the entity: counting <c>Vendor.Frames</c> on a
/// list of entities either loads every frame to count them or, if nobody
/// remembered the Include, quietly reports zero for all of them.
/// </summary>
public sealed class VendorRow
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Code { get; init; }
    public string? ContactName { get; init; }
    public string? Phone { get; init; }
    public int? LeadTimeDays { get; init; }
    public bool IsActive { get; init; }
    public int FrameCount { get; init; }
}

/// <summary>A supplier record, as the back office edits it.</summary>
public sealed class VendorDetails
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public int? LeadTimeDays { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Which frames a stock filter is asking for.</summary>
public enum StockFilter
{
    Any,
    InStock,
    OutOfStock,
    Low,
}

/// <summary>
/// Where the frame sits inside its own artwork, all as fractions of the image.
///
/// Six numbers, and none of them is a taste judgement: the two lens optical
/// centres, the two edges of the frame front, and the top and bottom of the
/// lens opening. Together with the millimetres on the frame record they let the
/// renderer draw the frame at its true size, so getting them right matters more
/// than any styling on this screen.
/// </summary>
public sealed class TryOnCalibrationInput
{
    public double LeftLensCenterX { get; set; }
    public double LeftLensCenterY { get; set; }
    public double RightLensCenterX { get; set; }
    public double RightLensCenterY { get; set; }

    public double FrontLeftX { get; set; }
    public double FrontRightX { get; set; }

    public double LensTopY { get; set; }
    public double LensBottomY { get; set; }

    /// <summary>Lower for tinted lenses, which should not hide the eyes entirely.</summary>
    public double Opacity { get; set; } = 1.0;

    public string? ImageUrl { get; set; }

    /// <summary>Natural size of the artwork, so the fit can be checked server-side.</summary>
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
}

public sealed class LensOptionDetails
{
    public string Group { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public double? MinSphere { get; set; }
    public double? MaxSphere { get; set; }
    public double? MaxCylinder { get; set; }
    public string? Requires { get; set; }
    public string? Excludes { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int Position { get; set; }
}

public interface ICatalogueAdminService
{
    Task<PagedResult<Frame>> ListFramesAsync(string? q, string? status, int page,
        StockFilter stock = StockFilter.Any,
        CancellationToken ct = default);

    Task<Frame?> GetFrameAsync(string id, CancellationToken ct = default);

    Task<IReadOnlyList<VendorRow>> ListVendorsAsync(bool activeOnly = false, CancellationToken ct = default);
    Task<Vendor?> GetVendorAsync(string id, CancellationToken ct = default);
    Task<ActionResult<string>> SaveVendorAsync(string? id, VendorDetails details, CancellationToken ct = default);
    Task<ActionResult> RetireVendorAsync(string id, CancellationToken ct = default);
    Task<ActionResult<string>> SaveFrameAsync(string? id, FrameDetails details, CancellationToken ct = default);
    Task<ActionResult> ArchiveFrameAsync(string id, CancellationToken ct = default);

    Task<ActionResult<string>> SaveVariantAsync(string frameId, string? variantId,
        VariantDetails details, CancellationToken ct = default);

    Task<ActionResult> SaveTryOnCalibrationAsync(
        string variantId, TryOnCalibrationInput input, CancellationToken ct = default);

    Task<IReadOnlyList<LensOption>> ListLensOptionsAsync(CancellationToken ct = default);
    Task<ActionResult> SaveLensOptionAsync(string? id, LensOptionDetails details, CancellationToken ct = default);
    Task<ActionResult> RetireLensOptionAsync(string id, CancellationToken ct = default);
}

/// <summary>
/// The catalogue screens. Port of the product half of
/// <c>src/app/actions/admin.ts</c>.
///
/// Money arrives from a form in major units and is converted exactly once, here,
/// with <see cref="Money.ToMinor"/>. Nothing downstream ever sees a decimal price.
/// </summary>
public sealed class CatalogueAdminService(
    IApplicationDbContext db,
    IAuditService audit,
    Microsoft.Extensions.Options.IOptions<Pricing.StoreOptions> store) : ICatalogueAdminService
{
    private readonly string _currency = store.Value.Currency;

    public async Task<PagedResult<Frame>> ListFramesAsync(
        string? q, string? status, int page,
        StockFilter stock = StockFilter.Any, CancellationToken ct = default)
    {
        var query = db.Frames.AsNoTracking()
            .Include(f => f.Brand)
            .Include(f => f.Vendor)
            .Include(f => f.Variants)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(f =>
                EF.Functions.Like(f.Name, $"%{term}%")
                || EF.Functions.Like(f.Sku, $"%{term}%")
                || f.Variants.Any(v => EF.Functions.Like(v.Sku, $"%{term}%")));
        }

        if (!string.IsNullOrEmpty(status)) query = query.Where(f => f.Status == status);

        // Stock is held per colourway, so a frame counts as in stock when any
        // of its live colourways does. Filtering on the sum would call a frame
        // available while the only colour anyone wants is gone.
        query = stock switch
        {
            StockFilter.InStock => query.Where(f => f.Variants.Any(v => v.IsActive && v.StockQty > 0)),
            StockFilter.OutOfStock => query.Where(f => !f.Variants.Any(v => v.IsActive && v.StockQty > 0)),
            StockFilter.Low => query.Where(f => f.Variants.Any(v =>
                v.IsActive && v.StockQty > 0 && v.StockQty <= v.LowStockAt)),
            _ => query,
        };

        var total = await query.CountAsync(ct);
        const int perPage = 25;
        var current = Math.Max(1, page);

        var items = await query
            .OrderBy(f => f.Position).ThenBy(f => f.Name)
            .Skip((current - 1) * perPage).Take(perPage)
            .ToListAsync(ct);

        return new PagedResult<Frame> { Items = items, Total = total, Page = current, PerPage = perPage };
    }

    public async Task<Frame?> GetFrameAsync(string id, CancellationToken ct = default) =>
        await db.Frames
            .Include(f => f.Brand)
            .Include(f => f.Vendor)
            .Include(f => f.Categories)
            .Include(f => f.Variants.OrderBy(v => v.Position)).ThenInclude(v => v.Images)
            .FirstOrDefaultAsync(f => f.Id == id, ct);

    // --- vendors -----------------------------------------------------------

    public async Task<IReadOnlyList<VendorRow>> ListVendorsAsync(
        bool activeOnly = false, CancellationToken ct = default) =>
        await db.Vendors.AsNoTracking()
            .Where(v => !activeOnly || v.IsActive)
            .OrderBy(v => v.Name)
            .Select(v => new VendorRow
            {
                Id = v.Id,
                Name = v.Name,
                Code = v.Code,
                ContactName = v.ContactName,
                Phone = v.Phone,
                LeadTimeDays = v.LeadTimeDays,
                IsActive = v.IsActive,
                FrameCount = v.Frames.Count,
            })
            .ToListAsync(ct);

    public async Task<Vendor?> GetVendorAsync(string id, CancellationToken ct = default) =>
        await db.Vendors.Include(v => v.Frames).FirstOrDefaultAsync(v => v.Id == id, ct);

    public async Task<ActionResult<string>> SaveVendorAsync(
        string? id, VendorDetails d, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(d.Name))
            return ActionResult<string>.Fail("Give the vendor a name.");

        var vendor = id is null
            ? new Vendor()
            : await db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct);

        if (vendor is null) return ActionResult<string>.Fail("That vendor no longer exists.");

        var name = d.Name.Trim();
        var code = Blank(d.Code)?.Trim().ToLowerInvariant();

        if (await db.Vendors.AnyAsync(v => v.Id != vendor.Id && v.Name == name, ct))
            return ActionResult<string>.Fail($"There is already a vendor called {name}.");

        if (code is not null && await db.Vendors.AnyAsync(v => v.Id != vendor.Id && v.Code == code, ct))
            return ActionResult<string>.Fail($"The code {code} belongs to another vendor.");

        if (d.LeadTimeDays is < 0 or > 365)
            return ActionResult<string>.Fail("Lead time should be between 0 and 365 days.");

        vendor.Name = name;
        vendor.Code = code;
        vendor.ContactName = Blank(d.ContactName);
        vendor.Email = Blank(d.Email);
        vendor.Phone = Blank(d.Phone);
        vendor.Address = Blank(d.Address);
        vendor.LeadTimeDays = d.LeadTimeDays;
        vendor.Notes = Blank(d.Notes);
        vendor.IsActive = d.IsActive;

        if (id is null) db.Vendors.Add(vendor);
        await db.SaveChangesAsync(ct);

        return ActionResult<string>.Success(vendor.Id);
    }

    /// <summary>
    /// Retires a vendor without touching its frames. The stock is still on the
    /// shelf and still sellable; only the reorder route has closed.
    /// </summary>
    public async Task<ActionResult> RetireVendorAsync(string id, CancellationToken ct = default)
    {
        var vendor = await db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vendor is null) return ActionResult.Fail("That vendor no longer exists.");

        vendor.IsActive = false;
        await db.SaveChangesAsync(ct);
        return ActionResult.Success();
    }

    public async Task<ActionResult<string>> SaveFrameAsync(
        string? id, FrameDetails d, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return ActionResult<string>.Fail("Give the frame a name.");
        if (d.Price <= 0) return ActionResult<string>.Fail("Enter a price above zero.");
        if (!ProductStatuses.All.Contains(d.Status)) return ActionResult<string>.Fail("Invalid status.");

        var frame = id is null
            ? new Frame()
            : await db.Frames.Include(f => f.Categories).FirstOrDefaultAsync(f => f.Id == id, ct);

        if (frame is null) return ActionResult<string>.Fail("That frame no longer exists.");

        var isNew = id is null;

        frame.Name = d.Name.Trim();
        frame.Sku = string.IsNullOrWhiteSpace(d.Sku) ? GenerateSku(d.Name) : d.Sku.Trim().ToUpperInvariant();
        frame.Slug = string.IsNullOrWhiteSpace(d.Slug) ? Slugify(d.Name) : Slugify(d.Slug);
        frame.BrandId = string.IsNullOrWhiteSpace(d.BrandId) ? null : d.BrandId;
        frame.Status = d.Status;
        frame.Position = d.Position;
        frame.Description = Blank(d.Description);
        frame.Shape = Blank(d.Shape);
        frame.Material = Blank(d.Material);
        frame.RimType = d.RimType;
        frame.Gender = d.Gender;
        frame.FaceShapes = Blank(d.FaceShapes);
        // Purchasing. Cost arrives in major units from a form and is converted
        // exactly once, here, like every other price on the way in.
        frame.VendorId = Blank(d.VendorId);
        frame.VendorProductCode = Blank(d.VendorProductCode)?.ToUpperInvariant();
        frame.LastCostMinor = d.LastCost is { } lastCost and > 0 ? Money.ToMinor(lastCost, _currency) : null;

        frame.PromotionGrade = PromotionGrades.All.Contains(d.PromotionGrade ?? "")
            ? d.PromotionGrade
            : null;

        // Only codes the domain knows. A typo in a form field must not become a
        // feature the product page then tries to explain.
        var features = d.FeatureCodes
            .Where(c => FrameFeatures.All.Contains(c))
            .Distinct()
            .ToList();
        frame.Features = features.Count > 0 ? string.Join(",", features) : null;

        frame.LensWidthMm = d.LensWidthMm;
        frame.BridgeWidthMm = d.BridgeWidthMm;
        frame.TempleLengthMm = d.TempleLengthMm;
        frame.LensHeightMm = d.LensHeightMm;
        frame.TotalWidthMm = d.TotalWidthMm;
        frame.WeightGrams = d.WeightGrams;

        // Money crosses from major to minor units exactly once, here.
        frame.BasePriceMinor = Money.ToMinor(d.Price, _currency);
        frame.CompareAtMinor = d.CompareAt is { } c ? Money.ToMinor(c, _currency) : null;
        frame.CostMinor = d.Cost is { } cost ? Money.ToMinor(cost, _currency) : null;

        frame.AllowFrameOnly = d.AllowFrameOnly;
        frame.RequiresPrescription = d.RequiresPrescription;
        frame.IsFeatured = d.IsFeatured;
        frame.MetaTitle = Blank(d.MetaTitle);
        frame.MetaDesc = Blank(d.MetaDesc);
        frame.SizeBand = d.TotalWidthMm is { } w
            ? w < 130 ? SizeBands.Narrow : w > 143 ? SizeBands.Wide : SizeBands.Medium
            : null;
        frame.SearchText = $"{frame.Name} {frame.Shape} {frame.Material} {frame.Gender}".ToLowerInvariant();

        if (isNew) db.Frames.Add(frame);

        // Uniqueness is enforced by the database; check first so staff get a
        // sentence rather than a constraint violation.
        var clash = await db.Frames.AnyAsync(
            f => f.Id != frame.Id && (f.Sku == frame.Sku || f.Slug == frame.Slug), ct);
        if (clash) return ActionResult<string>.Fail("Another frame already uses that SKU or URL slug.");

        await db.SaveChangesAsync(ct);

        // Replace the category set.
        var existing = await db.FrameCategories.Where(fc => fc.FrameId == frame.Id).ToListAsync(ct);
        db.FrameCategories.RemoveRange(existing);
        foreach (var categoryId in d.CategoryIds.Distinct())
            db.FrameCategories.Add(new FrameCategory { FrameId = frame.Id, CategoryId = categoryId });

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.PriceUpdate, "Frame", frame.Id, new
        {
            sku = frame.Sku,
            basePriceMinor = frame.BasePriceMinor,
            status = frame.Status,
            isNew,
        }, ct);

        return ActionResult<string>.Success(frame.Id);
    }

    /// <summary>
    /// Archives rather than deletes.
    ///
    /// A frame that has ever been ordered cannot be removed — the database
    /// refuses it, because order lines hold a restrictive reference. Archiving is
    /// what staff actually want anyway: the frame leaves the shop and the history
    /// stays readable.
    /// </summary>
    public async Task<ActionResult> ArchiveFrameAsync(string id, CancellationToken ct = default)
    {
        var frame = await db.Frames.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (frame is null) return ActionResult.Fail("That frame no longer exists.");

        frame.Status = ProductStatuses.Archived;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.PriceUpdate, "Frame", frame.Id,
            new { sku = frame.Sku, archived = true }, ct);

        return ActionResult.Success();
    }

    public async Task<ActionResult<string>> SaveVariantAsync(
        string frameId, string? variantId, VariantDetails d, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(d.ColorName))
            return ActionResult<string>.Fail("Give the colourway a name.");

        var frame = await db.Frames.FirstOrDefaultAsync(f => f.Id == frameId, ct);
        if (frame is null) return ActionResult<string>.Fail("That frame no longer exists.");

        var variant = variantId is null
            ? new FrameVariant { FrameId = frameId }
            : await db.FrameVariants.FirstOrDefaultAsync(v => v.Id == variantId, ct);

        if (variant is null) return ActionResult<string>.Fail("That colourway no longer exists.");

        variant.ColorName = d.ColorName.Trim();
        variant.ColorHex = Blank(d.ColorHex);
        variant.Sku = string.IsNullOrWhiteSpace(d.Sku)
            ? $"{frame.Sku}-{d.ColorName.Trim().ToUpperInvariant()[..Math.Min(3, d.ColorName.Trim().Length)]}"
            : d.Sku.Trim().ToUpperInvariant();
        variant.Barcode = Blank(d.Barcode);
        variant.PriceMinor = d.PriceOverride is { } p ? Money.ToMinor(p, _currency) : null;
        variant.StockQty = Math.Max(0, d.StockQty);
        variant.LowStockAt = Math.Max(0, d.LowStockAt);
        variant.Aisle = Blank(d.Aisle);
        variant.Shelf = Blank(d.Shelf);
        variant.ShelfRow = Blank(d.ShelfRow);
        variant.Bin = Blank(d.Bin);
        variant.Position = d.Position;
        variant.IsActive = d.IsActive;

        if (variantId is null) db.FrameVariants.Add(variant);

        var clash = await db.FrameVariants.AnyAsync(v => v.Id != variant.Id && v.Sku == variant.Sku, ct);
        if (clash) return ActionResult<string>.Fail("Another colourway already uses that SKU.");

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.PriceUpdate, "FrameVariant", variant.Id, new
        {
            sku = variant.Sku,
            stockQty = variant.StockQty,
            priceMinor = variant.PriceMinor,
        }, ct);

        return ActionResult<string>.Success(variant.Id);
    }

    /// <summary>
    /// Sets the two anchors that tell the mirror where the wearer's pupils must
    /// land inside this colourway's artwork. Without this screen a new frame can
    /// never appear in the try-on.
    /// </summary>
    public async Task<ActionResult> SaveTryOnCalibrationAsync(
        string variantId, TryOnCalibrationInput input, CancellationToken ct = default)
    {
        var variant = await db.FrameVariants.FirstOrDefaultAsync(v => v.Id == variantId, ct);
        if (variant is null) return ActionResult.Fail("That colourway no longer exists.");

        // Every figure is a fraction of the image. Anything outside 0..1 would
        // place part of the frame off the artwork entirely.
        double[] fractions =
        [
            input.LeftLensCenterX, input.LeftLensCenterY,
            input.RightLensCenterX, input.RightLensCenterY,
            input.FrontLeftX, input.FrontRightX,
            input.LensTopY, input.LensBottomY,
        ];

        if (fractions.Any(v => v is < 0 or > 1 || !double.IsFinite(v)))
            return ActionResult.Fail("Every marker must sit inside the image.");

        if (Math.Abs(input.RightLensCenterX - input.LeftLensCenterX) < 0.02)
            return ActionResult.Fail("The two lens centres are too close together to solve a fit.");

        if (input.FrontRightX - input.FrontLeftX < 0.05)
            return ActionResult.Fail("The frame front is too narrow to measure against.");

        if (input.LensBottomY - input.LensTopY < 0.02)
            return ActionResult.Fail("The lens opening is too shallow to measure against.");

        // The lens centres must be inside the frame front, or the picture is
        // saying the lenses are outside the frame.
        if (input.LeftLensCenterX < input.FrontLeftX || input.RightLensCenterX > input.FrontRightX)
            return ActionResult.Fail("The lens centres must sit inside the frame front.");

        variant.AnchorLeftX = input.LeftLensCenterX;
        variant.AnchorLeftY = input.LeftLensCenterY;
        variant.AnchorRightX = input.RightLensCenterX;
        variant.AnchorRightY = input.RightLensCenterY;
        variant.TryOnFrontLeftX = input.FrontLeftX;
        variant.TryOnFrontRightX = input.FrontRightX;
        variant.TryOnLensTopY = input.LensTopY;
        variant.TryOnLensBottomY = input.LensBottomY;
        variant.TryOnOpacity = Math.Clamp(input.Opacity, 0.1, 1.0);

        if (input.ImageWidth is > 0) variant.TryOnImageWidth = input.ImageWidth;
        if (input.ImageHeight is > 0) variant.TryOnImageHeight = input.ImageHeight;
        if (!string.IsNullOrWhiteSpace(input.ImageUrl)) variant.TryOnImageUrl = input.ImageUrl.Trim();

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.PriceUpdate, "FrameVariant", variant.Id,
            new { sku = variant.Sku, calibrated = true }, ct);

        return ActionResult.Success();
    }

    public async Task<IReadOnlyList<LensOption>> ListLensOptionsAsync(CancellationToken ct = default) =>
        await db.LensOptions.AsNoTracking()
            .OrderBy(o => o.Group).ThenBy(o => o.Position)
            .ToListAsync(ct);

    public async Task<ActionResult> SaveLensOptionAsync(
        string? id, LensOptionDetails d, CancellationToken ct = default)
    {
        if (!LensGroups.All.Contains(d.Group)) return ActionResult.Fail("Choose a valid lens step.");
        if (string.IsNullOrWhiteSpace(d.Code)) return ActionResult.Fail("Give the option a code.");
        if (string.IsNullOrWhiteSpace(d.Name)) return ActionResult.Fail("Give the option a name.");

        var option = id is null
            ? new LensOption()
            : await db.LensOptions.FirstOrDefaultAsync(o => o.Id == id, ct);

        if (option is null) return ActionResult.Fail("That lens option no longer exists.");

        option.Group = d.Group;
        option.Code = d.Code.Trim().ToLowerInvariant();
        option.Name = d.Name.Trim();
        option.Description = Blank(d.Description);
        option.PriceMinor = Money.ToMinor(d.Price, _currency);
        option.MinSphere = d.MinSphere;
        option.MaxSphere = d.MaxSphere;
        option.MaxCylinder = d.MaxCylinder;
        option.Requires = Blank(d.Requires);
        option.Excludes = Blank(d.Excludes);
        option.IsDefault = d.IsDefault;
        option.IsActive = d.IsActive;
        option.Position = d.Position;

        if (id is null) db.LensOptions.Add(option);

        var clash = await db.LensOptions.AnyAsync(o => o.Id != option.Id && o.Code == option.Code, ct);
        if (clash) return ActionResult.Fail("Another lens option already uses that code.");

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.PriceUpdate, "LensOption", option.Id,
            new { code = option.Code, priceMinor = option.PriceMinor }, ct);

        return ActionResult.Success();
    }

    /// <summary>
    /// Retires rather than deletes: order lines reference lens options by code,
    /// and a deleted row would make a historical invoice unreadable.
    /// </summary>
    public async Task<ActionResult> RetireLensOptionAsync(string id, CancellationToken ct = default)
    {
        var option = await db.LensOptions.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (option is null) return ActionResult.Fail("That lens option no longer exists.");

        option.IsActive = false;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditActions.PriceUpdate, "LensOption", option.Id,
            new { code = option.Code, retired = true }, ct);

        return ActionResult.Success();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GenerateSku(string name) =>
        "VC-" + new string([.. name.ToUpperInvariant().Where(char.IsAsciiLetterOrDigit)])
            [..Math.Min(4, name.Count(char.IsAsciiLetterOrDigit))];

    private static string Slugify(string value)
    {
        var slug = new string([.. value.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]);
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}
