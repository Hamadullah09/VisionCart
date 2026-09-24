using SkiaSharp;
using VisionCart.Infrastructure.Storage;

namespace VisionCart.UnitTests;

/// <summary>
/// The background remover turns a photograph of a frame into try-on artwork.
///
/// Two properties matter more than the cut-out quality itself. It must not erase
/// the frame (a pale frame on a pale backdrop), and it must not leave the lens
/// openings opaque (a white disc over each of the customer's eyes). Both fail
/// silently in production and are discovered by a shopper, so both are asserted
/// here — including the refusals, which are a feature rather than an error path.
/// </summary>
public class BackgroundRemoverTests
{
    private const int Size = 200;

    /// <summary>
    /// A stand-in for a pair of glasses: two rims with hollow centres, joined by
    /// a bridge, on a plain backdrop. Enough structure to exercise the outside,
    /// the enclosed openings and the connectivity guard.
    /// </summary>
    private static SKBitmap Spectacles(SKColor background, SKColor frame, SKColor lens)
    {
        var bitmap = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(background);

        using var rim = new SKPaint { Color = frame, IsAntialias = false, Style = SKPaintStyle.Fill };
        using var hole = new SKPaint { Color = lens, IsAntialias = false, Style = SKPaintStyle.Fill };

        // Left rim, right rim, and the bridge between them.
        canvas.DrawRect(new SKRect(30, 80, 90, 130), rim);
        canvas.DrawRect(new SKRect(110, 80, 170, 130), rim);
        canvas.DrawRect(new SKRect(90, 98, 110, 106), rim);

        // Hollow each rim out. These are enclosed by frame on all four sides.
        canvas.DrawRect(new SKRect(38, 88, 82, 122), hole);
        canvas.DrawRect(new SKRect(118, 88, 162, 122), hole);

        canvas.Flush();
        return bitmap;
    }

    private static byte AlphaAt(SKBitmap bitmap, int x, int y) => bitmap.GetPixel(x, y).Alpha;

    /// <summary>
    /// The same frame with an arm showing through each lens. This is what a
    /// camera sees: the pair is photographed open, so the temples recede from
    /// the hinges and the glass shows them running down and inward.
    /// </summary>
    private static SKBitmap SpectaclesWithArmsBehindTheLenses(
        SKColor background, SKColor frame, SKColor lens)
    {
        var bitmap = Spectacles(background, frame, lens);
        using var canvas = new SKCanvas(bitmap);
        using var arm = new SKPaint
        {
            Color = frame,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 5,
        };

        canvas.DrawLine(41, 91, 70, 118, arm);    // across the left lens
        canvas.DrawLine(159, 91, 130, 118, arm);  // and the right
        canvas.Flush();
        return bitmap;
    }

    [Fact]
    public void A_frame_on_a_plain_white_background_is_cut_out()
    {
        using var source = Spectacles(SKColors.White, SKColors.Black, SKColors.White);

        var result = BackgroundRemover.Remove(source);

        Assert.Equal(BackgroundRemovalOutcome.Removed, result.Outcome);
        using var cut = result.Bitmap!;

        Assert.Equal(0, AlphaAt(cut, 2, 2));          // corner — background
        Assert.Equal(0, AlphaAt(cut, 100, 10));       // above the frame
        Assert.Equal(255, AlphaAt(cut, 35, 105));     // left rim — frame
        Assert.Equal(255, AlphaAt(cut, 165, 105));    // right rim — frame
        Assert.Equal(255, AlphaAt(cut, 100, 102));    // the bridge
    }

    [Fact]
    public void The_lens_openings_are_cleared_so_the_customer_can_see_through_them()
    {
        // The whole point. A border-only flood fill leaves these opaque, which
        // puts a white disc over each eye in the mirror.
        using var source = Spectacles(SKColors.White, SKColors.Black, SKColors.White);

        var result = BackgroundRemover.Remove(source);

        using var cut = result.Bitmap!;
        Assert.Equal(2, result.OpeningsCleared);
        Assert.Equal(0, AlphaAt(cut, 60, 105));   // inside the left lens
        Assert.Equal(0, AlphaAt(cut, 140, 105));  // inside the right lens
    }

    [Fact]
    public void The_lens_openings_can_be_kept_opaque_when_asked()
    {
        using var source = Spectacles(SKColors.White, SKColors.Black, SKColors.White);

        var result = BackgroundRemover.Remove(source,
            new BackgroundRemovalOptions { ClearOpenings = false });

        using var cut = result.Bitmap!;
        Assert.Equal(0, result.OpeningsCleared);
        Assert.Equal(255, AlphaAt(cut, 60, 105));
        Assert.Equal(0, AlphaAt(cut, 2, 2));  // the outside still goes
    }

    [Fact]
    public void A_frame_coloured_part_that_matches_the_background_survives()
    {
        // Connectivity is what separates "white background" from "white temple
        // tip". A pixel the same colour as the backdrop but walled off from it
        // by the frame is part of the product.
        using var source = Spectacles(SKColors.White, SKColors.Black, SKColors.White);
        using (var canvas = new SKCanvas(source))
        {
            // A white square fully enclosed by the left rim, small enough to sit
            // under the minimum-opening threshold.
            using var white = new SKPaint { Color = SKColors.White, IsAntialias = false };
            canvas.DrawRect(new SKRect(40, 90, 44, 94), white);
            canvas.Flush();
        }

        var result = BackgroundRemover.Remove(source,
            new BackgroundRemovalOptions { ClearOpenings = false });

        using var cut = result.Bitmap!;
        Assert.Equal(255, AlphaAt(cut, 41, 91));
    }

    [Fact]
    public void A_pale_frame_on_a_pale_background_is_refused_rather_than_erased()
    {
        // Removing this would delete the product. Refusing is the correct answer,
        // and the contrast guard should catch it before anything is cut — the
        // removed-fraction guard behind it is only a backstop.
        using var source = Spectacles(
            SKColors.White, new SKColor(252, 252, 252), SKColors.White);

        var result = BackgroundRemover.Remove(source);

        Assert.Equal(BackgroundRemovalOutcome.InsufficientContrast, result.Outcome);
        Assert.Null(result.Bitmap);
        Assert.True(result.Separation < 64,
            $"separation was {result.Separation}, which should have been below the guard");
    }

    [Fact]
    public void A_forced_tolerance_bypasses_the_contrast_guard()
    {
        // The guard is keyed to the automatic threshold. Someone who states a
        // tolerance explicitly has taken the decision themselves.
        using var source = Spectacles(
            SKColors.White, new SKColor(200, 200, 200), SKColors.White);

        var auto = BackgroundRemover.Remove(source);
        Assert.Equal(BackgroundRemovalOutcome.InsufficientContrast, auto.Outcome);

        var forced = BackgroundRemover.Remove(source,
            new BackgroundRemovalOptions { Tolerance = 20, Feather = 10 });

        Assert.Equal(BackgroundRemovalOutcome.Removed, forced.Outcome);
        forced.Bitmap?.Dispose();
    }

    [Fact]
    public void A_busy_background_is_refused()
    {
        using var source = new SKBitmap(
            new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        // A deterministic chequerboard: no single border colour to key on.
        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            source.SetPixel(x, y, ((x / 8) + (y / 8)) % 2 == 0
                ? SKColors.White
                : new SKColor(20, 90, 160));
        }

        var result = BackgroundRemover.Remove(source);

        Assert.Equal(BackgroundRemovalOutcome.BackgroundNotPlain, result.Outcome);
        Assert.Null(result.Bitmap);
        Assert.True(result.BorderAgreement < 0.8);
    }

    [Fact]
    public void Artwork_that_is_already_cut_out_reports_that_there_is_nothing_to_do()
    {
        using var source = Spectacles(SKColors.Transparent, SKColors.Black, SKColors.Transparent);

        var result = BackgroundRemover.Remove(source);

        // Every transparent pixel is already at alpha 0, so no further alpha is
        // removed and the remover says so instead of rewriting the file.
        Assert.Equal(BackgroundRemovalOutcome.NothingToRemove, result.Outcome);
        Assert.Null(result.Bitmap);
    }

    [Fact]
    public void The_cut_edge_is_feathered_rather_than_jagged()
    {
        // A hard binary mask reads as a sticker in the mirror. Mid-tone pixels
        // between the frame and the backdrop should land between 0 and 255.
        using var source = Spectacles(SKColors.White, SKColors.Black, SKColors.White);
        using (var canvas = new SKCanvas(source))
        {
            // A grey ramp column sitting against the outside of the left rim.
            for (var y = 80; y < 130; y++)
            {
                var value = (byte)(255 - ((y - 80) * 5));
                using var paint = new SKPaint { Color = new SKColor(value, value, value) };
                canvas.DrawRect(new SKRect(26, y, 30, y + 1), paint);
            }
            canvas.Flush();
        }

        var result = BackgroundRemover.Remove(source);
        using var cut = result.Bitmap!;

        var alphas = new List<byte>();
        for (var y = 80; y < 130; y++) alphas.Add(AlphaAt(cut, 27, y));

        Assert.Contains(alphas, a => a is > 0 and < 255);
    }

    [Fact]
    public void An_existing_alpha_channel_is_never_made_more_opaque()
    {
        using var source = Spectacles(SKColors.White, SKColors.Black, SKColors.White);
        source.SetPixel(100, 60, new SKColor(255, 255, 255, 40)); // already part-transparent

        var result = BackgroundRemover.Remove(source);
        using var cut = result.Bitmap!;

        Assert.True(AlphaAt(cut, 100, 60) <= 40);
    }

    [Fact]
    public void The_result_reports_what_it_did()
    {
        using var source = Spectacles(SKColors.White, SKColors.Black, SKColors.White);

        var result = BackgroundRemover.Remove(source);

        Assert.True(result.Succeeded);
        Assert.InRange(result.RemovedFraction, 0.5, 0.99);
        Assert.Equal(SKColors.White, result.SampledBackground);
        Assert.Equal(1.0, result.BorderAgreement, 3);
    }

    /// <summary>
    /// A three-quarter view: the far lens is foreshortened and sits higher, and
    /// the visible near temple drags the opaque shape sideways relative to the
    /// pair. This is what a frame photographed at an angle looks like to the
    /// measurement, and it must not become try-on artwork.
    /// </summary>
    private static SKBitmap Angled()
    {
        var bitmap = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var rim = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        using var hole = new SKPaint { Color = SKColors.White, IsAntialias = false };

        canvas.DrawRect(new SKRect(20, 84, 86, 134), rim);   // near lens, full size
        canvas.DrawRect(new SKRect(96, 74, 140, 118), rim);  // far lens, smaller and higher
        canvas.DrawRect(new SKRect(140, 88, 185, 98), rim);  // near temple, visible

        canvas.DrawRect(new SKRect(28, 92, 78, 126), hole);
        canvas.DrawRect(new SKRect(103, 81, 133, 111), hole);

        canvas.Flush();
        return bitmap;
    }

    [Fact]
    public void A_frame_photographed_at_an_angle_is_refused()
    {
        using var source = Angled();

        var result = BackgroundRemover.Remove(source);

        Assert.Equal(BackgroundRemovalOutcome.NotFrontOn, result.Outcome);
        Assert.Null(result.Bitmap);
        Assert.True(result.FrontOnScore < 0.80,
            $"front-on score was {result.FrontOnScore:F3}, which should have failed the guard");
    }

    [Fact]
    public void An_angled_frame_can_still_be_cut_when_the_check_is_waived()
    {
        // The cut-out itself is fine; it is only unusable as try-on artwork. A
        // caller that wants the image for a gallery should still be able to ask.
        using var source = Angled();

        var result = BackgroundRemover.Remove(source,
            new BackgroundRemovalOptions { RequireFrontOn = false });

        Assert.Equal(BackgroundRemovalOutcome.Removed, result.Outcome);
        result.Bitmap?.Dispose();
    }

    [Fact]
    public void A_front_on_frame_scores_well_and_reports_its_lens_centres()
    {
        using var source = Spectacles(SKColors.White, SKColors.Black, SKColors.White);

        var result = BackgroundRemover.Remove(source);

        Assert.True(result.FrontOnScore > 0.80,
            $"front-on score was {result.FrontOnScore:F3}");

        // The openings are the calibration anchors, left to right.
        Assert.Equal(2, result.Openings.Count);
        Assert.True(result.Openings[0].CentreX < result.Openings[1].CentreX);

        // Symmetric about the middle of a symmetric drawing.
        var midpoint = (result.Openings[0].CentreX + result.Openings[1].CentreX) / 2;
        Assert.InRange(midpoint, 0.47, 0.53);

        // Level with each other.
        Assert.InRange(Math.Abs(result.Openings[0].CentreY - result.Openings[1].CentreY), 0, 0.01);

        result.Bitmap?.Dispose();
    }

    [Fact]
    public void An_empty_image_is_handled_without_throwing()
    {
        using var source = new SKBitmap(new SKImageInfo(0, 0));

        var result = BackgroundRemover.Remove(source);

        Assert.Equal(BackgroundRemovalOutcome.NothingToRemove, result.Outcome);
    }

    [Fact]
    public void A_dark_backdrop_works_as_well_as_a_light_one()
    {
        // Shops photograph pale frames on dark backgrounds; nothing in the
        // algorithm should assume white.
        using var source = Spectacles(
            new SKColor(12, 12, 14), SKColors.White, new SKColor(12, 12, 14));

        var result = BackgroundRemover.Remove(source);

        Assert.Equal(BackgroundRemovalOutcome.Removed, result.Outcome);
        using var cut = result.Bitmap!;
        Assert.Equal(0, AlphaAt(cut, 2, 2));
        Assert.Equal(255, AlphaAt(cut, 35, 105));
    }

    [Fact]
    public void An_arm_showing_through_a_lens_is_cleared_off_the_eye()
    {
        // The defect this guards: the arm is the frame own colour, so the
        // chroma key keeps it, and the mirror then paints it across the
        // wearer eye, which is the one place a temple never is.
        using var source = SpectaclesWithArmsBehindTheLenses(
            SKColors.White, SKColors.Black, SKColors.White);

        var result = BackgroundRemover.Remove(source);

        Assert.Equal(BackgroundRemovalOutcome.Removed, result.Outcome);
        using var cut = result.Bitmap!;

        Assert.True(result.TempleIntrusionPixels > 0);
        Assert.Equal(0, AlphaAt(cut, 55, 104));   // mid-arm, left lens
        Assert.Equal(0, AlphaAt(cut, 145, 104));  // mid-arm, right lens
    }

    [Fact]
    public void Clearing_an_arm_does_not_touch_the_frame_itself()
    {
        using var source = SpectaclesWithArmsBehindTheLenses(
            SKColors.White, SKColors.Black, SKColors.White);

        var result = BackgroundRemover.Remove(source);

        using var cut = result.Bitmap!;
        Assert.Equal(255, AlphaAt(cut, 35, 105));   // left rim
        Assert.Equal(255, AlphaAt(cut, 165, 105));  // right rim
        Assert.Equal(255, AlphaAt(cut, 100, 102));  // the bridge
        Assert.Equal(255, AlphaAt(cut, 60, 84));    // top of the left rim
    }

    [Fact]
    public void An_arm_running_off_the_side_is_left_where_it_is()
    {
        // Only what is inside a lens is an intrusion. The temples proper run
        // past the endpieces, outside every opening, and belong to the picture.
        using var source = SpectaclesWithArmsBehindTheLenses(
            SKColors.White, SKColors.Black, SKColors.White);
        using (var canvas = new SKCanvas(source))
        {
            using var arm = new SKPaint { Color = SKColors.Black, IsAntialias = false };
            canvas.DrawRect(new SKRect(8, 100, 30, 106), arm);
            canvas.DrawRect(new SKRect(170, 100, 192, 106), arm);
            canvas.Flush();
        }

        var result = BackgroundRemover.Remove(source);

        using var cut = result.Bitmap!;
        Assert.Equal(255, AlphaAt(cut, 15, 103));
        Assert.Equal(255, AlphaAt(cut, 185, 103));
    }

    [Fact]
    public void A_lens_is_measured_whole_even_when_an_arm_crosses_it()
    {
        // Why this matters beyond the picture: the opening centre is the pupil
        // anchor the mirror places the frame by. An arm eating the outer half of
        // the lens drags that centre inward, so the frame is fitted to a lens
        // centre that is not the lens.
        using var plain = Spectacles(SKColors.White, SKColors.Black, SKColors.White);
        using var armed = SpectaclesWithArmsBehindTheLenses(
            SKColors.White, SKColors.Black, SKColors.White);

        var truth = BackgroundRemover.Remove(plain);
        var cleared = BackgroundRemover.Remove(armed);
        var kept = BackgroundRemover.Remove(armed,
            new BackgroundRemovalOptions { ClearTempleIntrusions = false });

        var withClearing = Math.Abs(cleared.Openings[0].CentreX - truth.Openings[0].CentreX);
        var without = Math.Abs(kept.Openings[0].CentreX - truth.Openings[0].CentreX);

        Assert.True(withClearing < without,
            $"clearing the arm should recover the lens centre: {withClearing:F4} vs {without:F4}");
        Assert.Equal(truth.Openings[0].CentreX, cleared.Openings[0].CentreX, 2);
        Assert.Equal(truth.Openings[0].CentreY, cleared.Openings[0].CentreY, 2);
    }

    [Fact]
    public void Clearing_an_arm_does_not_move_the_frame_front()
    {
        // The front extent is the scale the mirror works from. An arm behind a
        // lens is well inside it either way, so removing one must not shift it.
        using var plain = Spectacles(SKColors.White, SKColors.Black, SKColors.White);
        using var armed = SpectaclesWithArmsBehindTheLenses(
            SKColors.White, SKColors.Black, SKColors.White);

        var truth = BackgroundRemover.Remove(plain);
        var cleared = BackgroundRemover.Remove(armed);

        Assert.Equal(truth.FrontLeftX, cleared.FrontLeftX, 3);
        Assert.Equal(truth.FrontRightX, cleared.FrontRightX, 3);
    }

    [Fact]
    public void The_arm_clear_out_can_be_turned_off()
    {
        using var source = SpectaclesWithArmsBehindTheLenses(
            SKColors.White, SKColors.Black, SKColors.White);

        var result = BackgroundRemover.Remove(source,
            new BackgroundRemovalOptions { ClearTempleIntrusions = false });

        using var cut = result.Bitmap!;
        Assert.Equal(0, result.TempleIntrusionPixels);
        Assert.Equal(255, AlphaAt(cut, 55, 104));
    }

    [Fact]
    public void An_opening_that_wraps_round_the_frame_is_left_alone()
    {
        // The guard. The fill assumes the opening is a lens: a shape with
        // nothing of the frame inside it. Give it a ring instead, with frame in
        // the middle, and filling between the ring own pixels would swallow the
        // lot. It must recognise that and do nothing rather than half of it.
        using var source = new SKBitmap(
            new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(source))
        {
            canvas.Clear(SKColors.White);
            using var frame = new SKPaint { Color = SKColors.Black, IsAntialias = false };
            using var gap = new SKPaint { Color = SKColors.White, IsAntialias = false };

            canvas.DrawRect(new SKRect(30, 40, 170, 170), frame);
            canvas.DrawCircle(100, 105, 50, gap);    // the ring outside
            canvas.DrawCircle(100, 105, 35, frame);  // frame island in the middle
            canvas.Flush();
        }

        var result = BackgroundRemover.Remove(source);

        Assert.Equal(BackgroundRemovalOutcome.Removed, result.Outcome);
        using var cut = result.Bitmap!;

        Assert.Equal(0, result.TempleIntrusionPixels);
        Assert.Equal(255, AlphaAt(cut, 100, 105));  // the island survives
        Assert.Equal(0, AlphaAt(cut, 100, 62));     // the ring itself still goes
    }
}
