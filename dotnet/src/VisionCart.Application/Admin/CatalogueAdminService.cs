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
        CancellationToken ct = default);

    Task<Frame?> GetFrameAsync(string id, CancellationToken ct = default);
    Task<ActionResult<string>> SaveFrameAsync(string? id, FrameDetails details, CancellationToken ct = default);
    Task<ActionResult> ArchiveFrameAsync(string id, CancellationToken ct = default);

    Task<ActionResult<string>> SaveVariantAsync(string frameId, string? variantId,
        VariantDetails details, CancellationToken ct = default);

    Task<ActionResult> SaveTryOnCalibrationAsync(string variantId, double leftX, double leftY,
        double rightX, double rightY, double scaleAdj, double opacity, string? imageUrl,
        CancellationToken ct = default);

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
        string? q, string? status, int page, CancellationToken ct = default)
    {
        var query = db.Frames.AsNoTracking()
            .Include(f => f.Brand)
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
            .Include(f => f.Categories)
            .Include(f => f.Variants.OrderBy(v => v.Position)).ThenInclude(v => v.Images)
            .FirstOrDefaultAsync(f => f.Id == id, ct);

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
        string variantId, double leftX, double leftY, double rightX, double rightY,
        double scaleAdj, double opacity, string? imageUrl, CancellationToken ct = default)
    {
        var variant = await db.FrameVariants.FirstOrDefaultAsync(v => v.Id == variantId, ct);
        if (variant is null) return ActionResult.Fail("That colourway no longer exists.");

        // Anchors are fractions of the image. Anything outside 0..1 would place
        // the frame off the artwork entirely.
        if (leftX is < 0 or > 1 || rightX is < 0 or > 1 || leftY is < 0 or > 1 || rightY is < 0 or > 1)
            return ActionResult.Fail("Anchor positions must sit inside the image.");

        if (Math.Abs(rightX - leftX) < 0.02)
            return ActionResult.Fail("The two anchors are too close together to solve a fit.");

        variant.AnchorLeftX = leftX;
        variant.AnchorLeftY = leftY;
        variant.AnchorRightX = rightX;
        variant.AnchorRightY = rightY;
        variant.TryOnScaleAdj = Math.Clamp(scaleAdj, 0.5, 2.0);
        variant.TryOnOpacity = Math.Clamp(opacity, 0.1, 1.0);
        if (!string.IsNullOrWhiteSpace(imageUrl)) variant.TryOnImageUrl = imageUrl.Trim();

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
