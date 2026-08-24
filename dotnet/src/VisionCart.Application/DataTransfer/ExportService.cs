using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VisionCart.Application.Common;
using VisionCart.Application.Platform;
using VisionCart.Application.Prescriptions;
using VisionCart.Application.Pricing;
using VisionCart.Domain.ValueObjects;

namespace VisionCart.Application.DataTransfer;

public sealed record CsvFile(string Filename, string Content);

public interface IExportService
{
    IReadOnlyList<(string Key, string Label, string Description)> Datasets { get; }
    Task<CsvFile> ExportAsync(string type, CancellationToken ct = default);
}

/// <summary>
/// Port of <c>src/app/api/admin/export/route.ts</c>.
///
/// Exports are round-trippable by design: export <em>Frames &amp; stock</em>, edit
/// prices in Excel, import the same file back. Rows are matched on
/// <c>variant_sku</c> or <c>file_no</c>, so re-importing updates rather than
/// duplicates.
///
/// Money is emitted in <b>major</b> units because a human edits these files; the
/// importer converts back at the edge. That is the only place in the system
/// where a price leaves minor units, and it is deliberate.
/// </summary>
public sealed class ExportService(
    IApplicationDbContext db,
    IAuditService audit,
    IOptions<StoreOptions> store,
    TimeProvider clock) : IExportService
{
    private readonly string _currency = store.Value.Currency;

    public IReadOnlyList<(string Key, string Label, string Description)> Datasets =>
    [
        ("frames", "Frames & stock", "One row per colourway. Edit prices or stock and import it back."),
        ("patients", "Patients", "One row per file, with the most recent prescription summarised."),
        ("prescriptions", "Prescriptions", "Every prescription version, full clinical detail."),
        ("orders", "Orders", "One row per order line, for accounting and lab reconciliation."),
    ];

    public async Task<CsvFile> ExportAsync(string type, CancellationToken ct = default)
    {
        var stamp = clock.GetUtcNow().ToString("yyyy-MM-dd");

        // Exporting patient or prescription data is itself an event worth
        // recording — these files leave the building.
        await audit.WriteAsync(
            type is "patients" or "prescriptions" ? AuditActions.ExportPatients : "data.export",
            "Export", null, new { type }, ct);

        return type switch
        {
            "frames" => new CsvFile($"frames-{stamp}.csv", await FramesAsync(ct)),
            "patients" => new CsvFile($"patients-{stamp}.csv", await PatientsAsync(ct)),
            "prescriptions" => new CsvFile($"prescriptions-{stamp}.csv", await PrescriptionsAsync(ct)),
            "orders" => new CsvFile($"orders-{stamp}.csv", await OrdersAsync(ct)),
            _ => throw new ArgumentException($"Unknown export \"{type}\".", nameof(type)),
        };
    }

    private decimal? Major(int? minor) => minor is { } m ? Money.FromMinor(m, _currency) : null;


    // Declared rather than inferred from the first row, so an empty dataset still
    // exports its header. ColumnsMatchTheProjection in the test suite asserts
    // these stay in step with the dictionaries below — the one risk of writing
    // them out twice.

    internal static readonly string[] FramesColumns =
    [
        "frame_sku",
        "frame_name",
        "brand",
        "variant_sku",
        "color_name",
        "color_hex",
        "price",
        "compare_at",
        "cost",
        "stock_qty",
        "shape",
        "material",
        "rim_type",
        "gender",
        "lens_width_mm",
        "bridge_width_mm",
        "temple_length_mm",
        "total_width_mm",
        "status",
        "try_on_image",
        "barcode",
    ];

    internal static readonly string[] PatientsColumns =
    [
        "file_no",
        "first_name",
        "last_name",
        "email",
        "phone",
        "date_of_birth",
        "pd_mm",
        "latest_prescription",
        "latest_rx_status",
        "latest_rx_date",
        "orders",
        "marketing_consent",
        "created",
    ];

    internal static readonly string[] PrescriptionsColumns =
    [
        "file_no",
        "patient",
        "issued",
        "expires",
        "status",
        "source",
        "od_sphere",
        "od_cylinder",
        "od_axis",
        "od_add",
        "od_pd",
        "os_sphere",
        "os_cylinder",
        "os_axis",
        "os_add",
        "os_pd",
        "pd_mm",
        "prescriber",
        "clinic",
    ];

    internal static readonly string[] OrdersColumns =
    [
        "order_no",
        "placed",
        "status",
        "payment_status",
        "patient_file",
        "email",
        "phone",
        "item",
        "item_sku",
        "lenses",
        "lab_status",
        "qty",
        "frame_price",
        "lens_price",
        "line_total",
        "order_total",
        "currency",
        "promo",
        "city",
        "country",
    ];

    private async Task<string> FramesAsync(CancellationToken ct)
    {
        var variants = await db.FrameVariants.AsNoTracking()
            .Include(v => v.Frame).ThenInclude(f => f.Brand)
            .OrderBy(v => v.Frame.Name).ThenBy(v => v.Position)
            .ToListAsync(ct);

        var rows = variants.Select(v => new Dictionary<string, object?>
        {
            ["frame_sku"] = v.Frame.Sku,
            ["frame_name"] = v.Frame.Name,
            ["brand"] = v.Frame.Brand?.Name ?? "",
            ["variant_sku"] = v.Sku,
            ["color_name"] = v.ColorName,
            ["color_hex"] = v.ColorHex ?? "",
            ["price"] = Major(v.PriceMinor ?? v.Frame.BasePriceMinor),
            ["compare_at"] = Major(v.Frame.CompareAtMinor),
            ["cost"] = Major(v.Frame.CostMinor),
            ["stock_qty"] = v.StockQty,
            ["shape"] = v.Frame.Shape ?? "",
            ["material"] = v.Frame.Material ?? "",
            ["rim_type"] = v.Frame.RimType,
            ["gender"] = v.Frame.Gender,
            ["lens_width_mm"] = v.Frame.LensWidthMm,
            ["bridge_width_mm"] = v.Frame.BridgeWidthMm,
            ["temple_length_mm"] = v.Frame.TempleLengthMm,
            ["total_width_mm"] = v.Frame.TotalWidthMm,
            ["status"] = v.Frame.Status,
            ["try_on_image"] = v.TryOnImageUrl ?? "",
            ["barcode"] = v.Barcode ?? "",
        }).ToList();

        return Csv.Write(rows, FramesColumns);
    }

    private async Task<string> PatientsAsync(CancellationToken ct)
    {
        var patients = await db.Patients.AsNoTracking()
            .Where(p => p.DeletedAt == null)
            .Include(p => p.Prescriptions.OrderByDescending(r => r.IssuedAt).Take(1))
            .OrderBy(p => p.FileNo)
            .Select(p => new
            {
                p.FileNo, p.FirstName, p.LastName, p.Email, p.Phone,
                p.DateOfBirth, p.PdMm, p.ConsentMarketing, p.CreatedAt,
                Orders = p.Orders.Count,
                Latest = p.Prescriptions.OrderByDescending(r => r.IssuedAt).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var rows = patients.Select(p => new Dictionary<string, object?>
        {
            ["file_no"] = p.FileNo,
            ["first_name"] = p.FirstName,
            ["last_name"] = p.LastName,
            ["email"] = p.Email ?? "",
            ["phone"] = p.Phone ?? "",
            ["date_of_birth"] = p.DateOfBirth?.ToString("yyyy-MM-dd") ?? "",
            ["pd_mm"] = p.PdMm,
            ["latest_prescription"] = p.Latest is null ? "" : Rx.Summarise(Rx.FromEntity(p.Latest)),
            ["latest_rx_status"] = p.Latest?.Status ?? "",
            ["latest_rx_date"] = p.Latest?.IssuedAt.ToString("yyyy-MM-dd") ?? "",
            ["orders"] = p.Orders,
            ["marketing_consent"] = p.ConsentMarketing ? "yes" : "no",
            ["created"] = p.CreatedAt.ToString("yyyy-MM-dd"),
        }).ToList();

        return Csv.Write(rows, PatientsColumns);
    }

    private async Task<string> PrescriptionsAsync(CancellationToken ct)
    {
        var prescriptions = await db.Prescriptions.AsNoTracking()
            .Include(r => r.Patient)
            .OrderByDescending(r => r.IssuedAt)
            .ToListAsync(ct);

        var rows = prescriptions.Select(r => new Dictionary<string, object?>
        {
            ["file_no"] = r.Patient.FileNo,
            ["patient"] = $"{r.Patient.FirstName} {r.Patient.LastName}".Trim(),
            ["issued"] = r.IssuedAt.ToString("yyyy-MM-dd"),
            ["expires"] = r.ExpiresAt?.ToString("yyyy-MM-dd") ?? "",
            ["status"] = r.Status,
            ["source"] = r.Source,
            ["od_sphere"] = r.OdSphere,
            ["od_cylinder"] = r.OdCylinder,
            ["od_axis"] = r.OdAxis,
            ["od_add"] = r.OdAdd,
            ["od_pd"] = r.OdPdMm,
            ["os_sphere"] = r.OsSphere,
            ["os_cylinder"] = r.OsCylinder,
            ["os_axis"] = r.OsAxis,
            ["os_add"] = r.OsAdd,
            ["os_pd"] = r.OsPdMm,
            ["pd_mm"] = r.Patient.PdMm,
            ["prescriber"] = r.Prescriber ?? "",
            ["clinic"] = r.Clinic ?? "",
        }).ToList();

        return Csv.Write(rows, PrescriptionsColumns);
    }

    private async Task<string> OrdersAsync(CancellationToken ct)
    {
        var orders = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Patient)
            .Include(o => o.ShippingAddress)
            .OrderByDescending(o => o.PlacedAt)
            .ToListAsync(ct);

        var rows = orders.SelectMany(o => o.Items.Select(i => new Dictionary<string, object?>
        {
            ["order_no"] = o.OrderNo,
            ["placed"] = o.PlacedAt,
            ["status"] = o.Status,
            ["payment_status"] = o.PaymentStatus,
            ["patient_file"] = o.Patient?.FileNo ?? "",
            ["email"] = o.Email,
            ["phone"] = o.Phone ?? "",
            ["item"] = i.TitleSnapshot,
            ["item_sku"] = i.SkuSnapshot,
            ["lenses"] = i.LensSummary ?? "",
            ["lab_status"] = i.LabStatus,
            ["qty"] = i.Qty,
            ["frame_price"] = Major(i.UnitPriceMinor),
            ["lens_price"] = Major(i.LensPriceMinor),
            ["line_total"] = Major(i.TotalMinor),
            ["order_total"] = Major(o.TotalMinor),
            ["currency"] = o.Currency,
            ["promo"] = o.PromoCode ?? "",
            ["city"] = o.ShippingAddress?.City ?? "",
            ["country"] = o.ShippingAddress?.Country ?? "",
        })).ToList();

        return Csv.Write(rows, OrdersColumns);
    }
}
