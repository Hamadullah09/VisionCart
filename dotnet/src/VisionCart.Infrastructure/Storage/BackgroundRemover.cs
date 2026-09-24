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
}

public sealed class BackgroundRemovalResult
{
    public required BackgroundRemovalOutcome Outcome { get; init; }

    /// <summary>The cut-out. Null unless <see cref="Outcome"/> is Removed.</summary>
    public SKBitmap? Bitmap { get; init; }

    /// <summary>Fraction of pixels made fully or partly transparent.</summary>
    public double RemovedFraction { get; init; }

    /// <summary>Enclosed regions cleared — normally 2 for a pair of lenses.</summary>
    public int OpeningsCleared { get; init; }

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

    public bool Succeeded => Outcome == BackgroundRemovalOutcome.Removed;
}

public static class BackgroundRemover
{
    // Marks in the working mask.
    private const byte Unvisited = 0;
    private const byte Outside = 1;   // background reachable from the border
    private const byte Kept = 2;      // the frame
    private const byte Opening = 3;   // enclosed background — the lenses

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

        var openings = 0;
        if (opts.ClearOpenings)
        {
            openings = ClearEnclosedOpenings(
                mask, distances, width, height, loose,
                (int)Math.Max(1, opts.MinOpeningFraction * total));
        }

        var output = new SKBitmap(
            new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var result = new SKColor[total];
        var removed = 0d;

        for (var i = 0; i < total; i++)
        {
            var px = pixels[i];

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
            OpeningsCleared = openings,
            SampledBackground = background,
            BorderAgreement = agreement,
            Separation = separation,
            ToleranceUsed = tolerance,
        };
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
    /// clears the ones big enough to be lens openings rather than noise.
    /// </summary>
    private static int ClearEnclosedOpenings(
        byte[] mask, int[] distances, int width, int height, int loose, int minArea)
    {
        var cleared = 0;
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
            cleared++;
        }

        return cleared;
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
