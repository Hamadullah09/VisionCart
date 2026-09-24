using SkiaSharp;

namespace VisionCart.Infrastructure.Storage;

/// <summary>
/// Turns a photograph of a frame on a plain background into try-on artwork:
/// background transparent, lens openings transparent, the frame itself opaque.
///
/// WHY THIS EXISTS. Try-on artwork is drawn over a customer's face, so anything
/// that is not the frame has to be transparent. The seeded catalogue gets that
/// for free because its artwork is generated (see tools/assets), but a shop
/// photographing its own stock uploads a JPEG, and a JPEG has no alpha channel
/// at all — every pixel is opaque. Without this the frame arrives in the mirror
/// as a white rectangle over the customer's eyes.
///
/// WHAT IT IS NOT. This is a chroma keyer, not a segmentation model. It assumes
/// the studio convention this kind of product photography already follows: one
/// frame, one plain background, reasonable contrast between them. It makes no
/// attempt at hair, cluttered desks or gradients. A model such as U²-Net would
/// handle those, and would also cost a ~44&#160;MB download, an ONNX runtime and
/// far more memory than a 512&#160;MB shared app pool has to spare — so the
/// cheap thing that works for the actual input is the right trade, provided it
/// REFUSES rather than guesses when its assumptions do not hold. That is what
/// <see cref="BackgroundRemovalOutcome"/> is for.
/// </summary>
public sealed class BackgroundRemovalOptions
{
    /// <summary>
    /// Fixed colour distance (0–255) at or below which a pixel counts as
    /// background. Leave null — the default — to derive it from the image with
    /// Otsu's method, which is what makes one setting work for a white backdrop
    /// and a charcoal one.
    ///
    /// A fixed threshold was the first implementation and it failed in a way
    /// worth recording: measured against all 72 pieces of catalogue artwork
    /// flattened onto three backdrops, it quietly kept as little as 51&#160;% of
    /// a low-contrast frame — eating the temples off an olive frame on charcoal
    /// while reporting success.
    /// </summary>
    public int? Tolerance { get; init; }

    /// <summary>
    /// Width of the soft edge above the tolerance. Pixels in this band get
    /// partial alpha, which is what stops the cut-out looking like it was done
    /// with scissors.
    /// </summary>
    public int Feather { get; init; } = 30;

    /// <summary>
    /// The tolerance never drops below this, so sensor noise and JPEG ringing in
    /// a flat backdrop still read as background.
    /// </summary>
    public int FloorTolerance { get; init; } = 18;

    /// <summary>
    /// The smallest Otsu threshold that counts as enough separation between the
    /// frame and its backdrop. Below it the cut is refused rather than attempted.
    ///
    /// 64 was measured, not guessed. Across 216 cases (72 frames × 3 backdrops)
    /// it admitted 103 and refused the rest; every one it admitted retained at
    /// least 99.2&#160;% of the frame, and none of the frame-eating cuts got
    /// through. Raising it further only refused good images.
    /// </summary>
    public int MinSeparation { get; init; } = 64;

    /// <summary>
    /// Clear background-coloured regions that are fully enclosed by the frame —
    /// in practice, the lens openings.
    ///
    /// This matters more than it sounds. A rim flood-fill alone leaves two
    /// opaque discs exactly where the customer's eyes are. The generated
    /// catalogue artwork keeps its lens area at about 10&#160;% alpha, so
    /// clearing it here is the closer match of the two available behaviours.
    /// </summary>
    public bool ClearOpenings { get; init; } = true;

    /// <summary>
    /// Enclosed regions smaller than this fraction of the image are left alone.
    /// Stops specular dots and JPEG noise inside the rim from being punched out.
    /// </summary>
    public double MinOpeningFraction { get; init; } = 0.0015;

    /// <summary>
    /// Clear the temple arms that show through the lenses.
    ///
    /// A pair of glasses is photographed open, so the arms recede behind the
    /// lenses and the camera sees them through the glass. The cut-out keeps
    /// them, because nothing about their colour says "backdrop" — and they land
    /// across the wearer's eyes in the mirror, which is precisely where a real
    /// temple never goes. Removing them leaves the frame front, which is what
    /// try-on artwork is.
    /// </summary>
    public bool ClearTempleIntrusions { get; init; } = true;

    /// <summary>
    /// Abandon the clear-out for a lens if it would take more than this share of
    /// that lens's opening.
    ///
    /// A safety net, not a tuned figure. The fill only spreads between pixels of
    /// one opening, so on any lens-shaped aperture it can reach the arm and
    /// nothing else; the cap is here for the aperture that is not lens-shaped —
    /// one wrapping round a piece of the frame, where the fill would bridge the
    /// gap and eat it. Measured on the frames to hand it takes 6.4&#160;% and
    /// 7.1&#160;% of the lens on one, 10.1&#160;% and 10.7&#160;% on the other,
    /// so a third of the opening is far beyond any real arm and still well short
    /// of the runaway it guards against.
    /// </summary>
    public double MaxIntrusionFraction { get; init; } = 0.35;

    /// <summary>
    /// Clear the temple arms that run off the sides of the frame front.
    ///
    /// A drawn piece of artwork can sweep its arms back along the side of the
    /// head, where they belong. A photograph cannot: the camera is in front, so
    /// the arms are foreshortened into short spikes at lens height, and the
    /// mirror has nowhere to put them except across the wearer's temples. What
    /// is left without them is the frame front, which is the part being tried
    /// on.
    /// </summary>
    public bool ClearTemples { get; init; } = true;

    /// <summary>
    /// Leave the temples alone unless the frame front is at least this much of
    /// the picture's full width.
    ///
    /// The guard on the measurement rather than on the result. Everything
    /// outside the front is about to be erased, so a front measured too narrow
    /// would take the frame with it. On the artwork to hand the front is 73&#160;%
    /// of the opaque width at its narrowest — the generated catalogue, whose
    /// arms are drawn full length — and 83&#160;% and 98&#160;% on photographs.
    /// Two fifths is far below anything real and still catches a measurement
    /// that has collapsed.
    /// </summary>
    public double MinFrontSpanFraction { get; init; } = 0.4;

    /// <summary>
    /// Refuse a frame that was not photographed front-on.
    ///
    /// The mirror places artwork by mapping the customer's two pupils onto the
    /// two lens centres. A three-quarter view has the far lens foreshortened and
    /// the pair sitting off-centre, so that mapping skews the frame across the
    /// face — it looks broken rather than merely imperfect, and the cut-out is
    /// blameless, which makes it hard to diagnose from the result.
    /// </summary>
    public bool RequireFrontOn { get; init; } = true;

    /// <summary>
    /// Front-on score below which the photograph is rejected. Measured on real
    /// product photography: front-on shots scored 0.97 and above, a
    /// three-quarter view scored 0.36. Anything in between is ambiguous enough
    /// that refusing is the safer answer.
    /// </summary>
    public double MinFrontOnScore { get; init; } = 0.80;

    /// <summary>Refuse above this — the subject itself was being erased.</summary>
    public double MaxRemovedFraction { get; init; } = 0.93;

    /// <summary>Refuse below this — nothing was found to remove.</summary>
    public double MinRemovedFraction { get; init; } = 0.02;

    /// <summary>
    /// Minimum share of border pixels that must agree with the sampled
    /// background colour before the background counts as plain.
    /// </summary>
    public double MinBorderAgreement { get; init; } = 0.80;
}

public enum BackgroundRemovalOutcome
{
    /// <summary>The background was removed and the result is usable.</summary>
    Removed,

    /// <summary>The border was not one consistent colour — no plain background.</summary>
    BackgroundNotPlain,

    /// <summary>Almost everything matched the background; the frame would be erased.</summary>
    SubjectTooSimilar,

    /// <summary>Almost nothing matched; there was no background to remove.</summary>
    NothingToRemove,

    /// <summary>
    /// The frame and the backdrop are too close in tone to separate reliably.
    /// Attempting it would keep part of the frame and erase the rest.
    /// </summary>
    InsufficientContrast,

    /// <summary>
    /// The frame was photographed at an angle rather than front-on. The cut-out
    /// itself is fine; it simply cannot be used as try-on artwork, because the
    /// mirror maps two pupils onto two lens centres and a three-quarter view has
    /// one lens foreshortened.
    /// </summary>
    NotFrontOn,
}

/// <summary>
/// An enclosed transparent region — in practice a lens opening. Coordinates are
/// normalised to the image, so they can be used directly as calibration anchors.
/// </summary>
public sealed class BackgroundRemovalOpening
{
    public required double CentreX { get; init; }
    public required double CentreY { get; init; }
    public required double TopY { get; init; }
    public required double BottomY { get; init; }
    public required int AreaPixels { get; init; }
}

public sealed class BackgroundRemovalResult
{
    public required BackgroundRemovalOutcome Outcome { get; init; }

    /// <summary>The cut-out. Null unless <see cref="Outcome"/> is Removed.</summary>
    public SKBitmap? Bitmap { get; init; }

    /// <summary>
    /// Fraction of pixels the backdrop removal made fully or partly
    /// transparent. Temple arms cleared from inside the lenses are not in this
    /// figure; they are counted by <see cref="TempleIntrusionPixels"/>.
    /// </summary>
    public double RemovedFraction { get; init; }

    /// <summary>Enclosed regions cleared — normally 2 for a pair of lenses.</summary>
    public int OpeningsCleared { get; init; }

    /// <summary>
    /// Pixels of temple arm cleared from inside the lenses. Zero on artwork that
    /// never had any, and on a photograph where the clear-out was abandoned.
    /// </summary>
    public int TempleIntrusionPixels { get; init; }

    /// <summary>
    /// Pixels of temple arm cleared from outside the frame front.
    /// </summary>
    public int TempleSidePixels { get; init; }

    /// <summary>
    /// The openings themselves, left to right, normalised to the image. For a
    /// front-on pair these are the pupil positions the calibration screen would
    /// otherwise have to be told by hand.
    /// </summary>
    public IReadOnlyList<BackgroundRemovalOpening> Openings { get; init; } = [];

    /// <summary>
    /// How level and equal the two openings are. 1 is perfectly front-on; a
    /// three-quarter view falls well below <see cref="BackgroundRemovalOptions.MinFrontOnScore"/>.
    /// Zero when there were not two openings to compare.
    /// </summary>
    public double FrontOnScore { get; init; }

    /// <summary>The colour the background was judged to be.</summary>
    public SKColor SampledBackground { get; init; }

    /// <summary>Share of border pixels that agreed with that colour.</summary>
    public double BorderAgreement { get; init; }

    /// <summary>
    /// The separation Otsu's method found between backdrop and subject. Higher
    /// is better; below <see cref="BackgroundRemovalOptions.MinSeparation"/> the
    /// cut is refused.
    /// </summary>
    public int Separation { get; init; }

    /// <summary>The colour distance actually used as the background threshold.</summary>
    public int ToleranceUsed { get; init; }

    /// <summary>
    /// Left and right edge of the frame FRONT, normalised — not of the whole
    /// silhouette. The temples run off the sides and must be excluded: the
    /// mirror scales artwork so this span equals the frame's recorded width, so
    /// counting the temples in it draws the frame too large and walks the
    /// temples across the wearer's face.
    /// </summary>
    public double FrontLeftX { get; init; }

    public double FrontRightX { get; init; }

    public bool Succeeded => Outcome == BackgroundRemovalOutcome.Removed;
}

public static class BackgroundRemover
{
    // Marks in the working mask.
    private const byte Unvisited = 0;
    private const byte Outside = 1;   // background reachable from the border
    private const byte Kept = 2;      // the frame
    private const byte Opening = 3;   // enclosed background — the lenses
    private const byte Intrusion = 4; // opaque, but inside a lens — a temple arm

    public static BackgroundRemovalResult Remove(
        SKBitmap source, BackgroundRemovalOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var opts = options ?? new BackgroundRemovalOptions();

        var width = source.Width;
        var height = source.Height;
        var total = width * height;
        if (total == 0)
        {
            return new BackgroundRemovalResult
            {
                Outcome = BackgroundRemovalOutcome.NothingToRemove,
            };
        }

        var pixels = source.Pixels;

        // Artwork that is already cut out arrives with a transparent border. Its
        // RGB there is meaningless — a fully transparent pixel is usually stored
        // as black — so keying on colour would decide the backdrop is black and
        // then erase every dark frame in the catalogue. Answer from the alpha
        // channel before looking at colour at all.
        if (BorderIsAlreadyTransparent(pixels, width, height))
        {
            return new BackgroundRemovalResult
            {
                Outcome = BackgroundRemovalOutcome.NothingToRemove,
            };
        }

        var (background, agreement) = SampleBorder(pixels, width, height, opts.FloorTolerance);
        if (agreement < opts.MinBorderAgreement)
        {
            return new BackgroundRemovalResult
            {
                Outcome = BackgroundRemovalOutcome.BackgroundNotPlain,
                SampledBackground = background,
                BorderAgreement = agreement,
            };
        }

        var distances = new int[total];
        var histogram = new int[256];
        for (var i = 0; i < total; i++)
        {
            var d = Distance(pixels[i], background);
            distances[i] = d;
            histogram[d]++;
        }

        // Where does the backdrop end and the frame begin? Otsu answers it from
        // the image rather than from a constant, which is the only way one
        // setting serves a white backdrop and a charcoal one. The value it picks
        // also measures how separable the two are, so it doubles as the guard.
        var separation = OtsuThreshold(histogram, total);

        if (opts.Tolerance is null && separation < opts.MinSeparation)
        {
            return new BackgroundRemovalResult
            {
                Outcome = BackgroundRemovalOutcome.InsufficientContrast,
                SampledBackground = background,
                BorderAgreement = agreement,
                Separation = separation,
            };
        }

        // Sit the soft edge just below the boundary Otsu found: everything
        // clearly darker than it goes, everything clearly beyond it stays.
        var tolerance = opts.Tolerance
            ?? Math.Max(opts.FloorTolerance, separation - opts.Feather);

        // Connectivity is decided with the loose threshold so that the soft edge
        // band is reachable; how transparent each pixel ends up is decided by
        // its own distance afterwards.
        var loose = tolerance + opts.Feather;

        var mask = new byte[total];

        FloodFromBorder(mask, distances, width, height, loose);

        var openings = new List<BackgroundRemovalOpening>();
        var intrusionPixels = 0;
        if (opts.ClearOpenings)
        {
            openings = ClearEnclosedOpenings(
                mask, distances, width, height, loose, tolerance,
                (int)Math.Max(1, opts.MinOpeningFraction * total),
                opts.ClearTempleIntrusions, opts.MaxIntrusionFraction,
                out intrusionPixels);
        }

        // Where the frame front starts and ends. Wanted whatever happens next:
        // it is both half of the front-on test and the scale the mirror works
        // from, so it is reported on every successful result.
        var (frontLeftColumn, frontRightColumn) = FrameFrontColumns(mask, width, height);

        // The arms off the sides go now, while the front is still measured from
        // a picture that has them: take them away first and the columns they
        // occupied would no longer be there to be excluded.
        var templeSidePixels = 0;
        if (opts.ClearTemples && frontLeftColumn >= 0)
        {
            templeSidePixels = ClearTemples(
                mask, width, height, frontLeftColumn, frontRightColumn,
                opts.MinFrontSpanFraction);
        }

        var frameLeft = frontLeftColumn < 0 ? 0d : (double)frontLeftColumn / width;
        var frameRight = frontLeftColumn < 0 ? 1d : (double)frontRightColumn / width;

        // Is it front-on? Measured from the openings, so it costs nothing extra
        // and only applies once there is a pair of lenses to compare.
        var frontOn = 0d;
        if (openings.Count == 2)
        {
            frontOn = FrontOnScoreFor(openings, frameLeft, frameRight);

            if (opts.RequireFrontOn && frontOn < opts.MinFrontOnScore)
            {
                return new BackgroundRemovalResult
                {
                    Outcome = BackgroundRemovalOutcome.NotFrontOn,
                    SampledBackground = background,
                    BorderAgreement = agreement,
                    Separation = separation,
                    ToleranceUsed = tolerance,
                    Openings = openings,
                    OpeningsCleared = openings.Count,
                    TempleIntrusionPixels = intrusionPixels,
                    TempleSidePixels = templeSidePixels,
                    FrontOnScore = frontOn,
                    FrontLeftX = frameLeft,
                    FrontRightX = frameRight,
                };
            }
        }

        var output = new SKBitmap(
            new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var result = new SKColor[total];
        var removed = 0d;

        for (var i = 0; i < total; i++)
        {
            var px = pixels[i];

            if (mask[i] == Intrusion)
            {
                // A temple arm behind the lens. It is the frame's own colour, so
                // its transparency cannot be derived from the colour distance
                // the way every other pixel's is: it goes because of where it
                // is, and it goes completely.
                //
                // Deliberately not counted as removed. The two fractions below
                // ask whether the chroma key went wrong, and an arm taken out on
                // purpose is not evidence either way — counting it tipped a
                // legitimate 92&#160;%-backdrop photograph over the ceiling and
                // had the cut refused as having eaten its subject.
                result[i] = new SKColor(px.Red, px.Green, px.Blue, 0);
                continue;
            }

            if (mask[i] is not (Outside or Opening))
            {
                result[i] = px;
                continue;
            }

            var alpha = AlphaFor(distances[i], tolerance, opts.Feather);

            // An input that already had transparency keeps it — never make a
            // pixel more opaque than it arrived.
            if (px.Alpha < alpha) alpha = px.Alpha;

            removed += (255 - alpha) / 255d;
            result[i] = new SKColor(px.Red, px.Green, px.Blue, alpha);
        }

        var removedFraction = removed / total;

        if (removedFraction > opts.MaxRemovedFraction)
        {
            output.Dispose();
            return new BackgroundRemovalResult
            {
                Outcome = BackgroundRemovalOutcome.SubjectTooSimilar,
                RemovedFraction = removedFraction,
                SampledBackground = background,
                BorderAgreement = agreement,
                Separation = separation,
                ToleranceUsed = tolerance,
            };
        }

        if (removedFraction < opts.MinRemovedFraction)
        {
            output.Dispose();
            return new BackgroundRemovalResult
            {
                Outcome = BackgroundRemovalOutcome.NothingToRemove,
                RemovedFraction = removedFraction,
                SampledBackground = background,
                BorderAgreement = agreement,
                Separation = separation,
                ToleranceUsed = tolerance,
            };
        }

        output.Pixels = result;

        return new BackgroundRemovalResult
        {
            Outcome = BackgroundRemovalOutcome.Removed,
            Bitmap = output,
            RemovedFraction = removedFraction,
            OpeningsCleared = openings.Count,
            Openings = openings,
            TempleIntrusionPixels = intrusionPixels,
            TempleSidePixels = templeSidePixels,
            FrontOnScore = frontOn,
            FrontLeftX = frameLeft,
            FrontRightX = frameRight,
            SampledBackground = background,
            BorderAgreement = agreement,
            Separation = separation,
            ToleranceUsed = tolerance,
        };
    }

    /// <summary>
    /// Where the frame FRONT begins and ends, normalised.
    ///
    /// Not the bounding box. A frame photographed front-on is a tall middle —
    /// the rims and bridge — with two thin temples running off the sides at lens
    /// height, and the bounding box includes those temples. The mirror scales
    /// artwork so that this span equals the frame's recorded overall width, so
    /// measuring the temples into it draws the whole frame too large: on a
    /// tightly cropped product photograph the front is about 90&#160;% of the
    /// image where the fallback assumes 72&#160;%, a 26&#160;% over-scale that
    /// walks the temples across the wearer's eyes.
    ///
    /// So measure the height of each column of surviving pixels and keep the
    /// columns that are a decent fraction of the tallest. The front is tall; a
    /// temple is a few pixels thick.
    ///
    /// The fraction is measured, not guessed. At 0.20 this reproduces the
    /// calibration recorded by hand for the seeded catalogue — 0.136–0.864
    /// against a recorded 0.137–0.863, averaged over all 72 pieces of artwork,
    /// a span within 0.2&#160;%. Lower admits the temples, higher eats the
    /// endpieces.
    /// </summary>
    private static (int Left, int Right) FrameFrontColumns(
        byte[] mask, int width, int height, double share = 0.20)
    {
        var columns = new int[width];
        var tallest = 0;

        for (var x = 0; x < width; x++)
        {
            var count = 0;
            for (var y = 0; y < height; y++)
            {
                if (mask[(y * width) + x] is Outside or Opening or Intrusion) continue;
                count++;
            }

            columns[x] = count;
            if (count > tallest) tallest = count;
        }

        if (tallest == 0) return (-1, -1);

        var cutoff = tallest * share;
        var left = -1;
        var right = -1;

        for (var x = 0; x < width; x++)
        {
            if (columns[x] < cutoff) continue;
            if (left < 0) left = x;
            right = x;
        }

        return (left, right);
    }

    /// <summary>
    /// Clears whatever lies outside the frame front: the temple arms.
    ///
    /// The mirror has one honest place to put an arm photographed head-on, and
    /// that is nowhere. It is a few foreshortened pixels beside the endpiece,
    /// pointing at the camera rather than back towards the ear, so drawn at the
    /// size the front dictates it becomes a spike across the wearer's temple.
    /// Drawn artwork can sweep an arm along the side of the head; a photograph
    /// of one cannot be turned into that, so it goes.
    ///
    /// Refuses if the front came out narrower than
    /// <paramref name="minSpanFraction"/> of everything opaque. Everything
    /// outside the front is about to be erased, which makes a front measured too
    /// narrow the one failure that would take the frame with it.
    /// </summary>
    private static int ClearTemples(
        byte[] mask, int width, int height, int frontLeft, int frontRight,
        double minSpanFraction)
    {
        var first = -1;
        var last = -1;

        for (var x = 0; x < width; x++)
        {
            var occupied = false;
            for (var y = 0; y < height; y++)
            {
                if (mask[(y * width) + x] is Outside or Opening or Intrusion) continue;
                occupied = true;
                break;
            }

            if (!occupied) continue;
            if (first < 0) first = x;
            last = x;
        }

        if (first < 0) return 0;
        if (frontRight - frontLeft + 1 < minSpanFraction * (last - first + 1)) return 0;

        var cleared = 0;

        for (var y = 0; y < height; y++)
        {
            var row = y * width;

            for (var x = 0; x < width; x++)
            {
                if (x >= frontLeft && x <= frontRight) continue;

                var index = row + x;
                if (mask[index] is Outside or Opening or Intrusion) continue;

                mask[index] = Intrusion;
                cleared++;
            }
        }

        return cleared;
    }

    /// <summary>
    /// Otsu's method over the histogram of colour distances: the threshold that
    /// maximises the variance between "close to the backdrop" and "not".
    ///
    /// Two things come out of it. The threshold itself, which adapts the cut to
    /// the image; and its position, which says how far apart the two groups are
    /// and therefore whether cutting is safe at all.
    /// </summary>
    private static int OtsuThreshold(int[] histogram, long total)
    {
        long weightedSum = 0;
        for (var i = 0; i < histogram.Length; i++) weightedSum += (long)i * histogram[i];

        long backgroundSum = 0;
        long backgroundCount = 0;
        double bestVariance = -1;

        // The best variance is usually a plateau rather than a single point, and
        // on artwork with only two exact colours the plateau spans nearly the
        // whole range: every threshold between them separates the two classes
        // equally well. Keeping the first match would return 0 and make a
        // perfectly clean cut-out look like zero contrast, so track the whole
        // plateau and take its midpoint.
        var plateauStart = 0;
        var plateauEnd = 0;

        for (var i = 0; i < histogram.Length; i++)
        {
            backgroundCount += histogram[i];
            if (backgroundCount == 0) continue;

            var foregroundCount = total - backgroundCount;
            if (foregroundCount == 0) break;

            backgroundSum += (long)i * histogram[i];

            var backgroundMean = (double)backgroundSum / backgroundCount;
            var foregroundMean = (double)(weightedSum - backgroundSum) / foregroundCount;
            var spread = backgroundMean - foregroundMean;
            var variance = (double)backgroundCount * foregroundCount * spread * spread;

            // A relative epsilon: these variances run to 1e12, so an absolute
            // one would either never match or always match.
            if (variance > bestVariance * (1 + 1e-9))
            {
                bestVariance = variance;
                plateauStart = i;
                plateauEnd = i;
            }
            else if (variance >= bestVariance * (1 - 1e-9))
            {
                plateauEnd = i;
            }
        }

        return (plateauStart + plateauEnd) / 2;
    }

    /// <summary>
    /// True when the outermost ring is essentially transparent already, which is
    /// what a PNG that has been cut out looks like.
    /// </summary>
    private static bool BorderIsAlreadyTransparent(SKColor[] pixels, int width, int height)
    {
        var opaque = 0;
        var counted = 0;

        for (var x = 0; x < width; x++)
        {
            if (pixels[x].Alpha > 8) opaque++;
            if (pixels[((height - 1) * width) + x].Alpha > 8) opaque++;
            counted += 2;
        }

        for (var y = 0; y < height; y++)
        {
            if (pixels[y * width].Alpha > 8) opaque++;
            if (pixels[(y * width) + width - 1].Alpha > 8) opaque++;
            counted += 2;
        }

        return counted > 0 && (double)opaque / counted < 0.02;
    }

    /// <summary>
    /// Takes the median of the outermost two-pixel ring, and reports how much of
    /// that ring agrees with it. The median rather than the mean because a
    /// single dark object touching the edge should not drag the estimate.
    /// </summary>
    private static (SKColor Background, double Agreement) SampleBorder(
        SKColor[] pixels, int width, int height, int tolerance)
    {
        var ring = new List<SKColor>((width + height) * 4);
        var depth = Math.Min(2, Math.Min(width, height));

        for (var d = 0; d < depth; d++)
        {
            for (var x = 0; x < width; x++)
            {
                ring.Add(pixels[d * width + x]);
                ring.Add(pixels[(height - 1 - d) * width + x]);
            }

            for (var y = 0; y < height; y++)
            {
                ring.Add(pixels[y * width + d]);
                ring.Add(pixels[y * width + (width - 1 - d)]);
            }
        }

        var background = new SKColor(
            Median(ring, c => c.Red),
            Median(ring, c => c.Green),
            Median(ring, c => c.Blue));

        var agreed = ring.Count(c => Distance(c, background) <= tolerance);

        return (background, (double)agreed / ring.Count);
    }

    private static byte Median(List<SKColor> colours, Func<SKColor, byte> channel)
    {
        var values = colours.Select(channel).ToArray();
        Array.Sort(values);
        return values[values.Length / 2];
    }

    /// <summary>
    /// Four-connected flood from every border pixel that looks like background.
    /// Connectivity is the whole point: a white temple tip in the middle of the
    /// frame is the same colour as a white background but is not reachable from
    /// the edge, so it survives.
    /// </summary>
    private static void FloodFromBorder(
        byte[] mask, int[] distances, int width, int height, int loose)
    {
        var stack = new Stack<int>(Math.Max(64, (width + height) * 2));

        void Seed(int index)
        {
            if (mask[index] == Unvisited && distances[index] <= loose)
            {
                mask[index] = Outside;
                stack.Push(index);
            }
        }

        for (var x = 0; x < width; x++)
        {
            Seed(x);
            Seed((height - 1) * width + x);
        }

        for (var y = 0; y < height; y++)
        {
            Seed(y * width);
            Seed(y * width + width - 1);
        }

        while (stack.Count > 0)
        {
            var index = stack.Pop();
            var x = index % width;
            var y = index / width;

            if (x > 0) Seed(index - 1);
            if (x < width - 1) Seed(index + 1);
            if (y > 0) Seed(index - width);
            if (y < height - 1) Seed(index + width);
        }
    }

    /// <summary>
    /// Finds background-coloured regions the border flood could not reach, and
    /// clears the ones big enough to be lens openings rather than noise. Each
    /// one then has the temple arm behind it taken out, so what is left of the
    /// opening is the whole lens.
    /// </summary>
    private static List<BackgroundRemovalOpening> ClearEnclosedOpenings(
        byte[] mask, int[] distances, int width, int height, int loose, int tolerance,
        int minArea, bool clearIntrusions, double maxIntrusionFraction,
        out int intrusionPixels)
    {
        intrusionPixels = 0;
        var found = new List<BackgroundRemovalOpening>();
        var component = new List<int>();
        var stack = new Stack<int>();

        for (var start = 0; start < mask.Length; start++)
        {
            if (mask[start] != Unvisited || distances[start] > loose) continue;

            component.Clear();
            stack.Push(start);
            mask[start] = Kept; // provisionally; promoted to Opening if big enough

            while (stack.Count > 0)
            {
                var index = stack.Pop();
                component.Add(index);

                var x = index % width;
                var y = index / width;

                void Visit(int neighbour)
                {
                    if (mask[neighbour] == Unvisited && distances[neighbour] <= loose)
                    {
                        mask[neighbour] = Kept;
                        stack.Push(neighbour);
                    }
                }

                if (x > 0) Visit(index - 1);
                if (x < width - 1) Visit(index + 1);
                if (y > 0) Visit(index - width);
                if (y < height - 1) Visit(index + width);
            }

            if (component.Count < minArea) continue;

            foreach (var index in component) mask[index] = Opening;

            // Take the arm out before measuring, so the centre reported is the
            // middle of the lens rather than of the crescent the arm left over.
            if (clearIntrusions)
            {
                var reclaimed = ApertureIntrusions(
                    mask, distances, width, height, tolerance, component, maxIntrusionFraction);

                if (reclaimed is not null)
                {
                    foreach (var index in reclaimed) mask[index] = Intrusion;
                    component.AddRange(reclaimed);
                    intrusionPixels += reclaimed.Count;
                }
            }

            long sumX = 0, sumY = 0;
            int top = height, bottom = -1;

            foreach (var index in component)
            {
                sumX += index % width;
                var y = index / width;
                sumY += y;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }

            found.Add(new BackgroundRemovalOpening
            {
                CentreX = (double)sumX / component.Count / width,
                CentreY = (double)sumY / component.Count / height,
                TopY = (double)top / height,
                BottomY = (double)bottom / height,
                AreaPixels = component.Count,
            });
        }

        // Left to right, so a caller can treat [0] and [1] as the wearer's right
        // and left lens without re-sorting.
        found.Sort((a, b) => a.CentreX.CompareTo(b.CentreX));
        return found;
    }

    /// <summary>
    /// The frame's own pixels sitting inside a lens opening: the temple arm the
    /// camera saw through the glass.
    ///
    /// Glasses are photographed open, so each arm recedes from its hinge and
    /// shows through the lens as a wedge running down and inward. The chroma key
    /// keeps it — correctly, since it is the frame — but the mirror then paints
    /// it across the wearer's eye, which is the one place a temple never is.
    ///
    /// Finding it needs no notion of what an arm looks like, only of where the
    /// lens is. Take the pixels of this opening that end up fully transparent:
    /// they are the glass. Anything that is not glass but lies between two bits
    /// of glass, along a row or down a column, is inside the lens. Repeat until
    /// nothing more is caught, because each pixel taken lengthens the runs that
    /// the next pass measures, and a diagonal wedge is only unpicked a step at a
    /// time. The rim is never between two bits of its own glass, so it is never
    /// touched.
    ///
    /// Returns null if the fill should be abandoned: either it escaped into the
    /// outside background, which means the opening was not enclosed after all,
    /// or it exceeded <paramref name="maxFraction"/> of the opening. Both say
    /// the shape being filled is not a lens, and half-clearing a frame is worse
    /// than leaving it alone.
    /// </summary>
    private static List<int>? ApertureIntrusions(
        byte[] mask, int[] distances, int width, int height, int tolerance,
        List<int> opening, double maxFraction)
    {
        var rowFirst = new int[height];
        var rowLast = new int[height];
        var columnFirst = new int[width];
        var columnLast = new int[width];
        Array.Fill(rowFirst, -1);
        Array.Fill(columnFirst, -1);

        void Extend(int index)
        {
            var x = index % width;
            var y = index / width;

            if (rowFirst[y] < 0) { rowFirst[y] = x; rowLast[y] = x; }
            else if (x < rowFirst[y]) rowFirst[y] = x;
            else if (x > rowLast[y]) rowLast[y] = x;

            if (columnFirst[x] < 0) { columnFirst[x] = y; columnLast[x] = y; }
            else if (y < columnFirst[x]) columnFirst[x] = y;
            else if (y > columnLast[x]) columnLast[x] = y;
        }

        var glass = 0;
        foreach (var index in opening)
        {
            if (distances[index] > tolerance) continue; // only partly transparent
            glass++;
            Extend(index);
        }

        if (glass == 0) return null;

        var budget = (int)(maxFraction * glass);
        var taken = new HashSet<int>();
        var order = new List<int>();

        // Claims one pixel. False means give up on this opening entirely.
        bool Take(int index)
        {
            if (mask[index] == Outside) return false;
            if (!taken.Add(index)) return true;
            if (order.Count >= budget) return false;

            order.Add(index);
            Extend(index);
            return true;
        }

        bool IsGlass(int index) => mask[index] == Opening && distances[index] <= tolerance;

        // Eight passes is well past the point where a real arm stops yielding;
        // the loop exits on its own as soon as a pass claims nothing.
        for (var pass = 0; pass < 8; pass++)
        {
            var claimed = order.Count;

            for (var y = 0; y < height; y++)
            {
                if (rowFirst[y] < 0) continue;
                var last = rowLast[y];

                for (var x = rowFirst[y] + 1; x < last; x++)
                {
                    var index = (y * width) + x;
                    if (IsGlass(index) || taken.Contains(index)) continue;
                    if (!Take(index)) return null;
                }
            }

            for (var x = 0; x < width; x++)
            {
                if (columnFirst[x] < 0) continue;
                var last = columnLast[x];

                for (var y = columnFirst[x] + 1; y < last; y++)
                {
                    var index = (y * width) + x;
                    if (IsGlass(index) || taken.Contains(index)) continue;
                    if (!Take(index)) return null;
                }
            }

            if (order.Count == claimed) break;
        }

        return order;
    }

    /// <summary>
    /// How front-on the photograph is, from the two lens openings: 1 is perfect.
    ///
    /// Three independent signals, multiplied so that failing any one of them
    /// sinks the score:
    ///   - the openings should be the same size (the far lens foreshortens),
    ///   - they should be level with each other (roll),
    ///   - and their midpoint should sit at the middle of the frame front
    ///     (in a three-quarter view the near temple shows and the far one does not,
    ///      which shifts the whole opaque shape sideways relative to the lenses).
    /// </summary>
    private static double FrontOnScoreFor(
        IReadOnlyList<BackgroundRemovalOpening> openings, double frameLeft, double frameRight)
    {
        if (openings.Count != 2) return 0;

        var a = openings[0];
        var b = openings[1];

        var areaRatio = (double)Math.Min(a.AreaPixels, b.AreaPixels)
                        / Math.Max(a.AreaPixels, b.AreaPixels);

        var span = Math.Abs(b.CentreX - a.CentreX);
        if (span <= 0) return 0;
        var levelness = Math.Max(0, 1 - (Math.Abs(b.CentreY - a.CentreY) / span * 4));

        var width = frameRight - frameLeft;
        var centring = width <= 0
            ? 0
            : Math.Max(0, 1 - (Math.Abs(((a.CentreX + b.CentreX) / 2)
                                        - ((frameLeft + frameRight) / 2)) / width * 6));

        return areaRatio * levelness * centring;
    }

    /// <summary>
    /// 0 at or below the tolerance, 255 once clear of the feather band, linear
    /// between. This is what produces the anti-aliased edge.
    /// </summary>
    private static byte AlphaFor(int distance, int tolerance, int feather)
    {
        if (distance <= tolerance) return 0;
        if (feather <= 0 || distance >= tolerance + feather) return 255;

        return (byte)(255 * (distance - tolerance) / feather);
    }

    /// <summary>
    /// Weighted RGB distance, scaled to 0–255. Green is weighted most heavily
    /// because that is roughly how much of perceived brightness it carries, and
    /// it keeps a mid-grey background from reading as close to a mid-green frame.
    /// </summary>
    private static int Distance(SKColor a, SKColor b)
    {
        int dr = a.Red - b.Red;
        int dg = a.Green - b.Green;
        int db = a.Blue - b.Blue;

        return (int)Math.Sqrt(((dr * dr * 2) + (dg * dg * 4) + (db * db * 3)) / 9.0);
    }
}
