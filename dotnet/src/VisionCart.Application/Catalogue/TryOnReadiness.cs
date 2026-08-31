using VisionCart.Domain.Entities;

namespace VisionCart.Application.Catalogue;

/// <summary>
/// Whether a colourway's data can support an accurate try-on.
///
/// The mirror draws a frame at its recorded width in millimetres, seats it by
/// its lens aperture and reports decentration from its lens centres. Every one
/// of those needs a figure somebody typed in, and any of them can be missing,
/// mistyped or in the wrong unit. Rendering an obviously wrong frame anyway is
/// the worst outcome: the customer cannot tell it is wrong, and the person who
/// could fix it never finds out.
///
/// So this exists to tell an administrator, in the frame list and on the
/// calibration screen, exactly what is missing and what looks implausible.
///
/// The same rules run in the browser (<c>ClientApp/tryon/fit.ts</c>) because
/// the renderer has to make the same decision at draw time without a round
/// trip. Both are covered by tests; if you change a threshold, change it in
/// both.
/// </summary>
public static class TryOnReadiness
{
    /// <summary>Frames are not made outside this range of lens widths.</summary>
    public const double LensWidthMinMm = 30;
    public const double LensWidthMaxMm = 70;

    public const double BridgeMinMm = 10;
    public const double BridgeMaxMm = 30;

    /// <summary>A lens is never much deeper than it is wide, nor a fraction of it.</summary>
    public const double LensHeightMinRatio = 0.4;
    public const double LensHeightMaxRatio = 1.4;

    /// <summary>
    /// How far the artwork and the measurements may disagree before it matters.
    ///
    /// Correctly drawn artwork reads the same scale three ways: lens centres
    /// against lens+bridge, frame front against overall width, and lens
    /// aperture against lens height. Six per cent covers rounding a
    /// calibration to four decimals; beyond that the picture and the spec
    /// sheet describe different frames.
    /// </summary>
    public const double CalibrationTolerance = 0.06;

    public enum Severity { Warning, Blocking }

    public sealed record Issue(string Field, string Message, Severity Severity);

    public sealed record Report(
        bool CanFitPhysically,
        IReadOnlyList<Issue> Issues,
        double? PixelsPerMmFromLensCentres,
        double? PixelsPerMmFromFront,
        double? PixelsPerMmFromLensAperture,
        double? Spread)
    {
        public bool IsCalibrated =>
            PixelsPerMmFromLensCentres is not null
            && PixelsPerMmFromFront is not null
            && PixelsPerMmFromLensAperture is not null;

        public bool HasWarnings => Issues.Any(i => i.Severity == Severity.Warning);

        /// <summary>A one-line status for a list row.</summary>
        public string Summary =>
            !CanFitPhysically ? "Try-on setup incomplete"
            : HasWarnings ? "Try-on works, with warnings"
            : "Ready";
    }

    public static Report Inspect(Frame frame, FrameVariant variant)
    {
        var issues = new List<Issue>();

        var lens = Positive(frame.LensWidthMm);
        var bridge = Positive(frame.BridgeWidthMm);
        var lensHeight = Positive(frame.LensHeightMm);
        var total = Positive(frame.TotalWidthMm);

        if (string.IsNullOrWhiteSpace(variant.TryOnImageUrl))
        {
            issues.Add(new("TryOnImageUrl",
                "No try-on artwork has been uploaded for this colourway.", Severity.Blocking));
        }

        if (lens is null)
            issues.Add(new("LensWidthMm", "Lens width is missing.", Severity.Blocking));

        if (bridge is null)
            issues.Add(new("BridgeWidthMm", "Bridge width is missing.", Severity.Blocking));

        if (total is null)
        {
            issues.Add(new("TotalWidthMm",
                "Overall frame width is missing, so the frame is sized from the lens and bridge "
                + "and will draw a few millimetres narrow.", Severity.Warning));
        }

        if (lensHeight is null)
        {
            issues.Add(new("LensHeightMm",
                "Lens height is missing, so the frame cannot be seated at the right height on the nose.",
                Severity.Warning));
        }

        // A typo produces impossible combinations easily, and they are far
        // easier to catch here than to spot in a rendered frame.
        if (lens is { } l && total is { } t && t < l * 2 + (bridge ?? 0))
        {
            issues.Add(new("TotalWidthMm",
                $"Overall width ({t:0.#} mm) is less than two lenses plus the bridge "
                + $"({l * 2 + (bridge ?? 0):0.#} mm). One of the three is wrong.", Severity.Blocking));
        }

        if (lens is { } lw && lensHeight is { } lh
            && (lh > lw * LensHeightMaxRatio || lh < lw * LensHeightMinRatio))
        {
            issues.Add(new("LensHeightMm",
                $"A lens height of {lh:0.#} mm doesn't go with a {lw:0.#} mm lens width. Check the units.",
                Severity.Warning));
        }

        if (lens is { } lw2 && (lw2 < LensWidthMinMm || lw2 > LensWidthMaxMm))
        {
            issues.Add(new("LensWidthMm",
                $"A {lw2:0.#} mm lens is outside the range frames are normally made in "
                + $"({LensWidthMinMm:0}–{LensWidthMaxMm:0} mm).", Severity.Warning));
        }

        if (bridge is { } bw && (bw < BridgeMinMm || bw > BridgeMaxMm))
        {
            issues.Add(new("BridgeWidthMm",
                $"A {bw:0.#} mm bridge is outside the usual range "
                + $"({BridgeMinMm:0}–{BridgeMaxMm:0} mm).", Severity.Warning));
        }

        // --- does the picture agree with the numbers? -----------------------
        double? fromCentres = null, fromFront = null, fromAperture = null;

        var imageWidth = variant.TryOnImageWidth;
        var imageHeight = variant.TryOnImageHeight;

        if (imageWidth is > 0 && lens is { } cl && bridge is { } cb)
        {
            var px = Math.Abs(variant.AnchorRightX - variant.AnchorLeftX) * imageWidth.Value;
            if (px > 0) fromCentres = px / (cl + cb);
        }

        if (imageWidth is > 0 && total is { } tw
            && variant.TryOnFrontLeftX is { } fl && variant.TryOnFrontRightX is { } fr)
        {
            var px = Math.Abs(fr - fl) * imageWidth.Value;
            if (px > 0) fromFront = px / tw;
        }

        if (imageHeight is > 0 && lensHeight is { } ah
            && variant.TryOnLensTopY is { } top && variant.TryOnLensBottomY is { } bottom)
        {
            var px = Math.Abs(bottom - top) * imageHeight.Value;
            if (px > 0) fromAperture = px / ah;
        }

        if (!string.IsNullOrWhiteSpace(variant.TryOnImageUrl)
            && (variant.TryOnFrontLeftX is null || variant.TryOnLensTopY is null))
        {
            issues.Add(new("calibration",
                "This artwork has not been calibrated, so the frame is placed using the default "
                + "proportions rather than its own. Open the calibration screen to fix it.",
                Severity.Warning));
        }

        double[] readings = [.. new[] { fromCentres, fromFront, fromAperture }
            .Where(v => v is > 0)
            .Select(v => v!.Value)];

        double? spread = null;

        if (readings.Length >= 2)
        {
            var lo = readings.Min();
            var hi = readings.Max();
            spread = (hi - lo) / hi;

            if (spread > CalibrationTolerance)
            {
                issues.Add(new("calibration",
                    $"The artwork and the measurements disagree by {spread * 100:0}%. The frame will "
                    + "be drawn at its recorded width, but the lenses and bridge in the picture won't "
                    + "line up with the numbers. Re-run the calibration for this colourway.",
                    Severity.Warning));
            }
        }

        return new Report(
            CanFitPhysically: !issues.Any(i => i.Severity == Severity.Blocking),
            Issues: issues,
            PixelsPerMmFromLensCentres: fromCentres,
            PixelsPerMmFromFront: fromFront,
            PixelsPerMmFromLensAperture: fromAperture,
            Spread: spread);
    }

    private static double? Positive(double? v) =>
        v is { } d && double.IsFinite(d) && d > 0 ? d : null;
}
