using VisionCart.Domain.Constants;

namespace VisionCart.Web;

/// <summary>
/// One place that decides what colour a status is.
///
/// Before this, each view chose for itself — the dashboard painted a pending
/// order amber and left every other state grey, while the order list used a
/// different rule again. The result was that "green" meant nothing in
/// particular, which is the fastest way to make staff stop reading badges.
///
/// The statuses come from several unrelated constant groups (orders, payments,
/// the lab, prescriptions, appointments, imports, privacy requests) but they
/// share their vocabulary — <c>pending</c>, <c>ready</c>, <c>cancelled</c> —
/// and they should mean the same thing wherever they appear, so one map keyed
/// on the raw value serves all of them.
///
/// The grammar is:
///   ok        — finished, and finished well
///   progress  — moving, nothing to do
///   lab       — with the lab, which is progress somebody else owns
///   warn      — waiting on us
///   danger    — failed, refused or cancelled
///   plain     — inert; true of archived and superseded things
/// </summary>
public static class StatusStyle
{
    // Keyed on the raw string, so the families overlap by design: "pending",
    // "completed", "cancelled" and "draft" occur in several of the constant
    // groups and mean the same thing in each. Every key below is written once —
    // where two groups share a literal, the shared meaning is noted rather than
    // the entry being repeated, because the indexer form of this initialiser
    // overwrites silently and a duplicate would be invisible.
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // Waiting on somebody here. "pending" covers orders, the lab, imports,
        // shipments and privacy requests alike.
        ["pending"] = "chip-warn",
        [PaymentStatuses.Unpaid] = "chip-warn",
        [RxStatuses.PendingVerification] = "chip-warn",
        [DataSubjectRequestStatuses.InReview] = "chip-warn",
        ["quality_check"] = "chip-warn",
        ["qc"] = "chip-warn",

        // Moving, nothing to do.
        [PaymentStatuses.Authorized] = "chip-info",
        [AppointmentStatuses.Scheduled] = "chip-info",
        [ImportJobStatuses.Running] = "chip-progress",
        ["ready"] = "chip-progress",
        ["shipped"] = "chip-progress",
        ["label_created"] = "chip-progress",
        ["in_transit"] = "chip-progress",
        ["out_for_delivery"] = "chip-progress",
        ["packed"] = "chip-progress",

        // With the lab. Its own colour, because "in the lab" is the state a
        // dispenser is asked about most and it is not the same as "shipped".
        [OrderStatuses.InLab] = "chip-lab",
        ["lab_processing"] = "chip-lab",
        ["ordered"] = "chip-lab",
        ["surfacing"] = "chip-lab",
        ["coating"] = "chip-lab",
        ["glazing"] = "chip-lab",

        // Done. "paid" is green rather than blue: an order reaching it means
        // the money cleared, which is the thing a bookkeeper is scanning for.
        // "completed" serves appointments, imports and privacy requests.
        ["paid"] = "chip-ok",
        ["delivered"] = "chip-ok",
        ["completed"] = "chip-ok",
        [ProductStatuses.Active] = "chip-ok",
        [RxStatuses.Verified] = "chip-ok",

        // Gone wrong. "rejected" covers both a prescription and a privacy
        // request; "failed" covers payments and imports.
        ["cancelled"] = "chip-danger",
        ["rejected"] = "chip-danger",
        ["failed"] = "chip-danger",
        [AppointmentStatuses.NoShow] = "chip-danger",
        ["returned"] = "chip-danger",

        // Inert. "draft" is both a product and a prescription.
        ["draft"] = "chip-plain",
        ["refunded"] = "chip-plain",
        [ProductStatuses.Archived] = "chip-plain",
        [RxStatuses.Expired] = "chip-plain",
        [PaymentStatuses.PartiallyRefunded] = "chip-plain",
        [FulfilmentStatuses.Unfulfilled] = "chip-plain",
    };

    /// <summary>
    /// The chip modifier for a status, e.g. <c>chip-ok</c>. Falls back to the
    /// neutral chip, so a value added to the constants file still renders as a
    /// badge rather than throwing.
    /// </summary>
    public static string For(string? status) =>
        status is not null && Map.TryGetValue(status, out var css) ? css : "chip-plain";

    /// <summary>The full class attribute, ready to drop into a view.</summary>
    public static string Chip(string? status) => $"chip {For(status)}";

    /// <summary>
    /// Ambiguous on its own: <c>pending</c> is amber for an order and for a
    /// prescription, but <c>cancelled</c> and <c>rejected</c> both read as a
    /// refusal. This exists so a caller can say which family it means when the
    /// shared map would be wrong.
    /// </summary>
    public static string ChipFor(string? status, string fallback) =>
        status is not null && Map.ContainsKey(status) ? Chip(status) : $"chip {fallback}";
}
