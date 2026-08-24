using VisionCart.Domain.Constants;
using VisionCart.Domain.ValueObjects;

namespace VisionCart.Domain.Entities;

public class Brand
{
    public string Id { get; set; } = Cuid.NewId();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? About { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Frame> Frames { get; set; } = [];
}

public class Category
{
    public string Id { get; set; } = Cuid.NewId();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = [];
    public int Position { get; set; }

    public ICollection<FrameCategory> Frames { get; set; } = [];
}

/// <summary>Join table between <see cref="Frame"/> and <see cref="Category"/>.</summary>
public class FrameCategory
{
    public string FrameId { get; set; } = string.Empty;
    public Frame Frame { get; set; } = null!;
    public string CategoryId { get; set; } = string.Empty;
    public Category Category { get; set; } = null!;
}

public class Frame
{
    public string Id { get; set; } = Cuid.NewId();
    public string Sku { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? BrandId { get; set; }
    public Brand? Brand { get; set; }

    public string? Description { get; set; }

    /// <summary>rectangle | square | round | oval | aviator | cat_eye | wayfarer | geometric | browline</summary>
    public string? Shape { get; set; }

    /// <summary>acetate | metal | titanium | tr90 | stainless | mixed | wood</summary>
    public string? Material { get; set; }

    /// <summary>full_rim | semi_rimless | rimless</summary>
    public string RimType { get; set; } = RimTypes.FullRim;

    /// <summary>men | women | unisex | kids</summary>
    public string Gender { get; set; } = Genders.Unisex;

    /// <summary>Face shapes this frame flatters, comma separated.</summary>
    public string? FaceShapes { get; set; }

    // Standard eyewear sizing in millimetres (e.g. 52 [] 18 - 140)
    public double? LensWidthMm { get; set; }
    public double? BridgeWidthMm { get; set; }
    public double? TempleLengthMm { get; set; }
    public double? LensHeightMm { get; set; }
    public double? TotalWidthMm { get; set; }
    public double? WeightGrams { get; set; }

    /// <summary>narrow | medium | wide — derived from TotalWidthMm, used for filters.</summary>
    public string? SizeBand { get; set; }

    // Money is stored in minor units (paisa/cents) to avoid float drift
    public int BasePriceMinor { get; set; }
    public int? CompareAtMinor { get; set; }
    public int? CostMinor { get; set; }

    public bool AllowFrameOnly { get; set; } = true;
    public bool RequiresPrescription { get; set; }

    /// <summary>draft | active | archived</summary>
    public string Status { get; set; } = ProductStatuses.Draft;

    public bool IsFeatured { get; set; }
    public int Position { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDesc { get; set; }

    /// <summary>
    /// Added during migration. Populated from Name/Sku/Description/Brand on save
    /// and indexed, so catalogue search is a single indexed predicate under SQL
    /// Server rather than four LIKE scans joined by OR.
    /// </summary>
    public string? SearchText { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<FrameVariant> Variants { get; set; } = [];
    public ICollection<FrameCategory> Categories { get; set; } = [];
}

/// <summary>
/// A colourway of a frame. Stock, images and the try-on overlay all hang here
/// because they are colour-specific.
/// </summary>
public class FrameVariant
{
    public string Id { get; set; } = Cuid.NewId();
    public string FrameId { get; set; } = string.Empty;
    public Frame Frame { get; set; } = null!;

    public string Sku { get; set; } = string.Empty;
    public string ColorName { get; set; } = string.Empty;

    /// <summary>Hex swatch shown in the colour picker.</summary>
    public string? ColorHex { get; set; }
    public string? Barcode { get; set; }

    /// <summary>Overrides the frame base price when set.</summary>
    public int? PriceMinor { get; set; }

    public int StockQty { get; set; }
    public int LowStockAt { get; set; } = 3;
    public bool IsActive { get; set; } = true;
    public int Position { get; set; }

    // --- Virtual try-on overlay calibration -------------------------------
    // A front-on PNG with transparency. The two anchors are the points in that
    // image where the wearer's pupils must land, expressed 0..1 of image size.
    // Everything else (scale, rotation, position) is solved from the detected
    // pupils, so a correctly anchored asset needs no per-face tuning.
    public string? TryOnImageUrl { get; set; }
    public double AnchorLeftX { get; set; } = 0.29;
    public double AnchorLeftY { get; set; } = 0.50;
    public double AnchorRightX { get; set; } = 0.71;
    public double AnchorRightY { get; set; } = 0.50;

    /// <summary>Multiplier applied after auto-fit, for assets with generous padding.</summary>
    public double TryOnScaleAdj { get; set; } = 1.0;

    /// <summary>Extra opacity for tinted/sunglass lenses drawn over the eyes.</summary>
    public double TryOnOpacity { get; set; } = 1.0;

    public ICollection<ProductImage> Images { get; set; } = [];
    public ICollection<CartItem> CartItems { get; set; } = [];
    public ICollection<OrderItem> OrderItems { get; set; } = [];
    public ICollection<TryOnSnapshot> Snapshots { get; set; } = [];

    /// <summary>A colourway only appears in the mirror once it has been calibrated.</summary>
    public bool IsTryOnReady => !string.IsNullOrWhiteSpace(TryOnImageUrl);
}

public class ProductImage
{
    public string Id { get; set; } = Cuid.NewId();
    public string VariantId { get; set; } = string.Empty;
    public FrameVariant Variant { get; set; } = null!;

    public string Url { get; set; } = string.Empty;
    public string? ThumbUrl { get; set; }
    public string? Alt { get; set; }

    /// <summary>gallery | primary | try_on | swatch | lifestyle | 360</summary>
    public string Role { get; set; } = ProductImageRoles.Gallery;

    public int? Width { get; set; }
    public int? Height { get; set; }
    public int Position { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A sellable lens configuration option. Grouped by <see cref="Group"/> so the
/// storefront renders the lens builder as a sequence of steps without hard-coding it.
/// </summary>
public class LensOption
{
    public string Id { get; set; } = Cuid.NewId();

    /// <summary>usage | type | index | coating | tint | extra</summary>
    public string Group { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceMinor { get; set; }

    // Only offered when the Rx falls inside this range (null = no limit)
    public double? MinSphere { get; set; }
    public double? MaxSphere { get; set; }
    public double? MaxCylinder { get; set; }

    /// <summary>Comma-separated codes of options this one requires.</summary>
    public string? Requires { get; set; }

    /// <summary>Comma-separated codes of options this one conflicts with.</summary>
    public string? Excludes { get; set; }

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int Position { get; set; }
}
