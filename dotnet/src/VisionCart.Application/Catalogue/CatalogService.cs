using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Common;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;

namespace VisionCart.Application.Catalogue;

public sealed class FrameFilters
{
    public string? Q { get; init; }
    public string? Gender { get; init; }
    public string? Shape { get; init; }
    public string? Material { get; init; }
    public string? RimType { get; init; }
    public string? Brand { get; init; }
    public string? Category { get; init; }
    public string? FaceShape { get; init; }
    public string? SizeBand { get; init; }
    public int? MinPrice { get; init; }
    public int? MaxPrice { get; init; }
    /// <summary>featured | price_asc | price_desc | newest</summary>
    public string? Sort { get; init; }
    public int Page { get; init; } = 1;
    public int PerPage { get; init; } = 24;
}

public sealed class FrameCard
{
    public string Id { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? BrandName { get; init; }
    public string? Shape { get; init; }
    public string? Material { get; init; }
    public int BasePriceMinor { get; init; }
    public int? CompareAtMinor { get; init; }
    public bool IsFeatured { get; init; }
    public DateTime CreatedAt { get; init; }
    public double? LensWidthMm { get; init; }
    public double? BridgeWidthMm { get; init; }
    public double? TempleLengthMm { get; init; }
    public IReadOnlyList<VariantCard> Variants { get; init; } = [];

    public bool IsOnSale => CompareAtMinor is { } c && c > BasePriceMinor;
    public int LowestPriceMinor => Variants.Count == 0
        ? BasePriceMinor
        : Variants.Min(v => v.PriceMinor ?? BasePriceMinor);

    /// <summary>Standard eyewear sizing, e.g. 52□18-140.</summary>
    public string SizeText =>
        LensWidthMm is null || BridgeWidthMm is null || TempleLengthMm is null
            ? string.Empty
            : $"{LensWidthMm:0}□18-{TempleLengthMm:0}".Replace("18", $"{BridgeWidthMm:0}");
}

public sealed class VariantCard
{
    public string Id { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public string ColorName { get; init; } = string.Empty;
    public string? ColorHex { get; init; }
    public int? PriceMinor { get; init; }
    public int StockQty { get; init; }
    public string? ImageUrl { get; init; }
    public string? ThumbUrl { get; init; }
    public bool IsTryOnReady { get; init; }
}

public sealed class CatalogueFacets
{
    public IReadOnlyList<(string Name, string Slug)> Brands { get; init; } = [];
    public IReadOnlyList<(string Value, int Count)> Shapes { get; init; } = [];
    public IReadOnlyList<(string Value, int Count)> Materials { get; init; } = [];
    public IReadOnlyList<(string Name, string Slug)> Categories { get; init; } = [];
}

/// <summary>A colourway with try-on artwork, shaped flat for the canvas.</summary>
public sealed class TryOnFrame
{
    public string VariantId { get; init; } = string.Empty;
    public string FrameId { get; init; } = string.Empty;
    public string FrameName { get; init; } = string.Empty;
    public string ColorName { get; init; } = string.Empty;
    public string? ColorHex { get; init; }
    public string Slug { get; init; } = string.Empty;
    public string? BrandName { get; init; }
    public string? ThumbUrl { get; init; }
    public int PriceMinor { get; init; }
    public string TryOnImageUrl { get; init; } = string.Empty;
    public double AnchorLeftX { get; init; }
    public double AnchorLeftY { get; init; }
    public double AnchorRightX { get; init; }
    public double AnchorRightY { get; init; }
    public double TryOnScaleAdj { get; init; }
    public double TryOnOpacity { get; init; }

    // Physical dimensions, millimetres. The mirror needs these to draw a frame
    // at its true size against the wearer's own PD rather than merely landing
    // its lens centres on the pupils — a frame can sit on the pupils correctly
    // and still be far too wide for the wearer.
    public double? LensWidthMm { get; init; }
    public double? BridgeWidthMm { get; init; }
    public double? TempleLengthMm { get; init; }
    public double? LensHeightMm { get; init; }
    public double? TotalWidthMm { get; init; }

    // Where the frame sits inside its own artwork. Without these the renderer
    // cannot tell frame from padding, so it cannot draw the frame to size.
    public double? FrontLeftX { get; init; }
    public double? FrontRightX { get; init; }
    public double? LensTopY { get; init; }
    public double? LensBottomY { get; init; }
}

public interface ICatalogService
{
    Task<PagedResult<FrameCard>> ListFramesAsync(FrameFilters filters, CancellationToken ct = default);
    Task<Frame?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<CatalogueFacets> GetFacetsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FrameCard>> FeaturedAsync(int take = 12, CancellationToken ct = default);
    Task<IReadOnlyList<TryOnFrame>> ListTryOnFramesAsync(int limit = 60, CancellationToken ct = default);
    Task<IReadOnlyList<LensOption>> LensBuilderOptionsAsync(CancellationToken ct = default);
}

/// <summary>
/// Port of <c>src/lib/catalog.ts</c>.
///
/// Shared catalogue reads, so views stay free of query plumbing. Everything here
/// is projected and paged — a shop with 10,000 frames must not load them all.
/// </summary>
public sealed class CatalogService(IApplicationDbContext db) : ICatalogService
{
    public async Task<PagedResult<FrameCard>> ListFramesAsync(
        FrameFilters filters, CancellationToken ct = default)
    {
        var perPage = Math.Clamp(filters.PerPage, 1, 60);
        var page = Math.Max(1, filters.Page);

        var query = db.Frames.AsNoTracking().Where(f => f.Status == ProductStatuses.Active);

        if (!string.IsNullOrWhiteSpace(filters.Q))
        {
            // SQL Server string comparison follows the database collation, which
            // is case-insensitive by default (…_CI_AS). The legacy code carried a
            // note that this becomes case-sensitive on PostgreSQL; that risk does
            // not apply here, and CatalogSearchTests pins the behaviour so a
            // future collation change cannot silently break search.
            var term = filters.Q.Trim();
            query = query.Where(f =>
                EF.Functions.Like(f.Name, $"%{term}%")
                || EF.Functions.Like(f.Sku, $"%{term}%")
                || (f.Description != null && EF.Functions.Like(f.Description, $"%{term}%"))
                || (f.Brand != null && EF.Functions.Like(f.Brand.Name, $"%{term}%"))
                || f.Variants.Any(v => EF.Functions.Like(v.Sku, $"%{term}%")
                                       || (v.Barcode != null && EF.Functions.Like(v.Barcode, $"%{term}%"))));
        }

        if (!string.IsNullOrEmpty(filters.Gender)) query = query.Where(f => f.Gender == filters.Gender);
        if (!string.IsNullOrEmpty(filters.Shape)) query = query.Where(f => f.Shape == filters.Shape);
        if (!string.IsNullOrEmpty(filters.Material)) query = query.Where(f => f.Material == filters.Material);
        if (!string.IsNullOrEmpty(filters.RimType)) query = query.Where(f => f.RimType == filters.RimType);
        if (!string.IsNullOrEmpty(filters.SizeBand)) query = query.Where(f => f.SizeBand == filters.SizeBand);
        if (!string.IsNullOrEmpty(filters.Brand)) query = query.Where(f => f.Brand!.Slug == filters.Brand);

        if (!string.IsNullOrEmpty(filters.Category))
            query = query.Where(f => f.Categories.Any(c => c.Category.Slug == filters.Category));

        if (!string.IsNullOrEmpty(filters.FaceShape))
            query = query.Where(f => f.FaceShapes != null
                                     && EF.Functions.Like(f.FaceShapes, $"%{filters.FaceShape}%"));

        if (filters.MinPrice is { } min) query = query.Where(f => f.BasePriceMinor >= min);
        if (filters.MaxPrice is { } max) query = query.Where(f => f.BasePriceMinor <= max);

        query = filters.Sort switch
        {
            "price_asc" => query.OrderBy(f => f.BasePriceMinor),
            "price_desc" => query.OrderByDescending(f => f.BasePriceMinor),
            "newest" => query.OrderByDescending(f => f.CreatedAt),
            _ => query.OrderByDescending(f => f.IsFeatured).ThenBy(f => f.Position).ThenByDescending(f => f.CreatedAt),
        };

        var total = await query.CountAsync(ct);

        var items = await query
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(Projection)
            .ToListAsync(ct);

        return new PagedResult<FrameCard> { Items = items, Total = total, Page = page, PerPage = perPage };
    }

    /// <summary>
    /// Projection rather than full entity loading: the catalogue grid needs about
    /// a dozen fields, not every column plus every relation.
    /// </summary>
    private static readonly System.Linq.Expressions.Expression<Func<Frame, FrameCard>> Projection =
        f => new FrameCard
        {
            Id = f.Id,
            Slug = f.Slug,
            Name = f.Name,
            BrandName = f.Brand != null ? f.Brand.Name : null,
            Shape = f.Shape,
            Material = f.Material,
            BasePriceMinor = f.BasePriceMinor,
            CompareAtMinor = f.CompareAtMinor,
            IsFeatured = f.IsFeatured,
            CreatedAt = f.CreatedAt,
            LensWidthMm = f.LensWidthMm,
            BridgeWidthMm = f.BridgeWidthMm,
            TempleLengthMm = f.TempleLengthMm,
            Variants = f.Variants
                .Where(v => v.IsActive)
                .OrderBy(v => v.Position)
                .Select(v => new VariantCard
                {
                    Id = v.Id,
                    Sku = v.Sku,
                    ColorName = v.ColorName,
                    ColorHex = v.ColorHex,
                    PriceMinor = v.PriceMinor,
                    StockQty = v.StockQty,
                    ImageUrl = v.Images.OrderBy(i => i.Position).Select(i => i.Url).FirstOrDefault(),
                    ThumbUrl = v.Images.OrderBy(i => i.Position).Select(i => i.ThumbUrl).FirstOrDefault(),
                    IsTryOnReady = v.TryOnImageUrl != null,
                })
                .ToList(),
        };

    public async Task<Frame?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        await db.Frames
            .AsNoTracking()
            .Include(f => f.Brand)
            .Include(f => f.Categories).ThenInclude(c => c.Category)
            .Include(f => f.Variants.Where(v => v.IsActive).OrderBy(v => v.Position))
                .ThenInclude(v => v.Images.OrderBy(i => i.Position))
            .FirstOrDefaultAsync(f => f.Slug == slug && f.Status == ProductStatuses.Active, ct);

    /// <summary>Filter facets, counted against the live catalogue so nothing dead is shown.</summary>
    public async Task<CatalogueFacets> GetFacetsAsync(CancellationToken ct = default)
    {
        var brands = await db.Brands.AsNoTracking()
            .Where(b => b.IsActive && b.Frames.Any(f => f.Status == ProductStatuses.Active))
            .OrderBy(b => b.Name)
            .Select(b => new { b.Name, b.Slug })
            .ToListAsync(ct);

        var shapes = await db.Frames.AsNoTracking()
            .Where(f => f.Status == ProductStatuses.Active && f.Shape != null)
            .GroupBy(f => f.Shape!)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var materials = await db.Frames.AsNoTracking()
            .Where(f => f.Status == ProductStatuses.Active && f.Material != null)
            .GroupBy(f => f.Material!)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.Frames.Any(fc => fc.Frame.Status == ProductStatuses.Active))
            .OrderBy(c => c.Position)
            .Select(c => new { c.Name, c.Slug })
            .ToListAsync(ct);

        return new CatalogueFacets
        {
            Brands = [.. brands.Select(b => (b.Name, b.Slug))],
            Shapes = [.. shapes.Select(s => (s.Value, s.Count))],
            Materials = [.. materials.Select(m => (m.Value, m.Count))],
            Categories = [.. categories.Select(c => (c.Name, c.Slug))],
        };
    }

    public async Task<IReadOnlyList<FrameCard>> FeaturedAsync(int take = 12, CancellationToken ct = default) =>
        await db.Frames.AsNoTracking()
            .Where(f => f.Status == ProductStatuses.Active)
            .OrderByDescending(f => f.IsFeatured).ThenBy(f => f.Position)
            .Take(take)
            .Select(Projection)
            .ToListAsync(ct);

    /// <summary>
    /// Every colourway that has try-on artwork, flattened for the canvas. A
    /// colourway without artwork simply doesn't appear in the mirror.
    /// </summary>
    public async Task<IReadOnlyList<TryOnFrame>> ListTryOnFramesAsync(
        int limit = 60, CancellationToken ct = default) =>
        await db.FrameVariants.AsNoTracking()
            .Where(v => v.IsActive
                        && v.TryOnImageUrl != null
                        && v.Frame.Status == ProductStatuses.Active)
            .OrderByDescending(v => v.Frame.IsFeatured).ThenBy(v => v.Frame.Position).ThenBy(v => v.Position)
            .Take(limit)
            .Select(v => new TryOnFrame
            {
                VariantId = v.Id,
                FrameId = v.FrameId,
                LensWidthMm = v.Frame.LensWidthMm,
                BridgeWidthMm = v.Frame.BridgeWidthMm,
                TempleLengthMm = v.Frame.TempleLengthMm,
                LensHeightMm = v.Frame.LensHeightMm,
                TotalWidthMm = v.Frame.TotalWidthMm,
                FrontLeftX = v.TryOnFrontLeftX,
                FrontRightX = v.TryOnFrontRightX,
                LensTopY = v.TryOnLensTopY,
                LensBottomY = v.TryOnLensBottomY,
                FrameName = v.Frame.Name,
                ColorName = v.ColorName,
                ColorHex = v.ColorHex,
                Slug = v.Frame.Slug,
                BrandName = v.Frame.Brand != null ? v.Frame.Brand.Name : null,
                ThumbUrl = v.Images.OrderBy(i => i.Position).Select(i => i.ThumbUrl ?? i.Url).FirstOrDefault(),
                PriceMinor = v.PriceMinor ?? v.Frame.BasePriceMinor,
                TryOnImageUrl = v.TryOnImageUrl!,
                AnchorLeftX = v.AnchorLeftX,
                AnchorLeftY = v.AnchorLeftY,
                AnchorRightX = v.AnchorRightX,
                AnchorRightY = v.AnchorRightY,
                TryOnScaleAdj = v.TryOnScaleAdj,
                TryOnOpacity = v.TryOnOpacity,
            })
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LensOption>> LensBuilderOptionsAsync(CancellationToken ct = default) =>
        await db.LensOptions.AsNoTracking()
            .Where(o => o.IsActive)
            .OrderBy(o => o.Group).ThenBy(o => o.Position)
            .ToListAsync(ct);
}
