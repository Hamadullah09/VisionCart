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
}
