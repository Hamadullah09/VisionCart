using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using VisionCart.Infrastructure.Persistence;

namespace VisionCart.IntegrationTests.Http;

/// <summary>
/// Uploading try-on artwork for a colourway, over real HTTP.
///
/// Before this route existed the only way to get a photograph onto a frame was
/// the media library plus a separate attach step, and the calibration screen
/// told staff to do it on the frame page — which had no upload control at all.
/// </summary>
[Collection("http")]
public class HttpArtworkUploadTests(VisionCartApp app)
{
    /// <summary>A frame-shaped subject on a plain white backdrop, as a JPEG.</summary>
    private static byte[] FramePhotograph(SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg)
    {
        using var bitmap = new SKBitmap(
            new SKImageInfo(480, 240, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var rim = new SKPaint { Color = SKColors.Black };
            using var hole = new SKPaint { Color = SKColors.White };

            canvas.DrawRect(new SKRect(60, 80, 200, 170), rim);
            canvas.DrawRect(new SKRect(280, 80, 420, 170), rim);
            canvas.DrawRect(new SKRect(200, 112, 280, 128), rim);
            canvas.DrawRect(new SKRect(75, 95, 185, 155), hole);
            canvas.DrawRect(new SKRect(295, 95, 405, 155), hole);
            canvas.Flush();
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 95);
        return data.ToArray();
    }

    /// <summary>
    /// Builds the multipart body, including the antiforgery token the global
    /// AutoValidateAntiforgeryToken filter requires — a post without one is
    /// rejected before it reaches the action, which is asserted separately in
    /// <see cref="HttpAntiforgeryTests"/>.
    /// </summary>
    private async Task<MultipartFormDataContent> BodyAsync(
        HttpClient client, string page, byte[] bytes, string filename,
        string contentType, bool removeBackground)
    {
        var token = await app.AntiforgeryTokenAsync(client, page);

        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        return new MultipartFormDataContent
        {
            { file, "file", filename },
            { new StringContent(removeBackground ? "true" : "false"), "removeBackground" },
            { new StringContent(token), "__RequestVerificationToken" },
        };
    }

    private async Task<(string FrameId, string VariantId)> AnyVariantAsync()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var variant = await db.FrameVariants.AsNoTracking().FirstAsync();
        return (variant.FrameId, variant.Id);
    }

    [Fact]
    public async Task The_calibration_page_offers_an_upload_control()
    {
        var (frameId, variantId) = await AnyVariantAsync();

        var html = await app.Admin.GetStringAsync(
            $"/admin/frames/{frameId}/variants/{variantId}/calibrate");

        Assert.Contains("type=\"file\"", html);
        Assert.Contains($"/admin/frames/{frameId}/variants/{variantId}/artwork", html);
        Assert.Contains("removeBackground", html);

        // The form must carry an antiforgery token or every upload is rejected.
        Assert.Contains("__RequestVerificationToken", html);
    }

    /// <summary>
    /// Puts a variant's try-on artwork back where it was.
    ///
    /// The harness runs as Development so it can read the seeded staff
    /// credentials, which means it shares the development database. Attaching
    /// artwork is a real write to a real catalogue row, so this test undoes
    /// itself rather than leaving a photograph of a test fixture on a frame
    /// somebody is looking at.
    /// </summary>
    private async Task RestoreArtworkAsync(string variantId, string? url)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var variant = await db.FrameVariants.FirstAsync(v => v.Id == variantId);
        variant.TryOnImageUrl = url;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_jpeg_photograph_is_cut_out_and_attached_as_try_on_artwork()
    {
        var (frameId, variantId) = await AnyVariantAsync();

        string? original;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            original = (await db.FrameVariants.AsNoTracking()
                .FirstAsync(v => v.Id == variantId)).TryOnImageUrl;
        }

        try
        {
            var page = $"/admin/frames/{frameId}/variants/{variantId}/calibrate";
            var response = await app.Admin.PostAsync(
                $"/admin/frames/{frameId}/variants/{variantId}/artwork",
                await BodyAsync(app.Admin, page, FramePhotograph(), "shopfloor.jpg",
                    "image/jpeg", removeBackground: true));

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var variant = await db.FrameVariants.AsNoTracking().FirstAsync(v => v.Id == variantId);

            Assert.NotNull(variant.TryOnImageUrl);

            // A JPEG has no alpha channel, so artwork derived from one is only usable
            // if it was stored as PNG — WebP here would mean the cut-out was lost.
            Assert.EndsWith(".png", variant.TryOnImageUrl);

            // And the stored file must actually be transparent where the backdrop was.
            var served = await app.Admin.GetByteArrayAsync(variant.TryOnImageUrl);
            using var stored = SKBitmap.Decode(served);

            Assert.Equal(0, stored.GetPixel(2, 2).Alpha);

            // Inside the left lens: the customer has to be able to see through it.
            // Coordinates are proportional because the master is resized on the way in.
            var lensX = (int)(stored.Width * 0.27);
            var lensY = (int)(stored.Height * 0.52);
            Assert.True(stored.GetPixel(lensX, lensY).Alpha < 128,
                $"lens interior alpha was {stored.GetPixel(lensX, lensY).Alpha}");

            // The rim itself must survive.
            var rimX = (int)(stored.Width * 0.14);
            Assert.True(stored.GetPixel(rimX, lensY).Alpha > 128,
                $"rim alpha was {stored.GetPixel(rimX, lensY).Alpha}");
        }
        finally
        {
            await RestoreArtworkAsync(variantId, original);
        }
    }

    [Fact]
    public async Task A_photograph_with_no_usable_background_is_refused_with_a_reason()
    {
        var (frameId, variantId) = await AnyVariantAsync();

        // Near-white frame on white: nothing to separate.
        using var bitmap = new SKBitmap(
            new SKImageInfo(320, 200, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var faint = new SKPaint { Color = new SKColor(251, 251, 251) };
            canvas.DrawRect(new SKRect(40, 60, 280, 140), faint);
            canvas.Flush();
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        var page = $"/admin/frames/{frameId}/variants/{variantId}/calibrate";
        var response = await app.Admin.PostAsync(
            $"/admin/frames/{frameId}/variants/{variantId}/artwork",
            await BodyAsync(app.Admin, page, data.ToArray(), "washed-out.png",
                "image/png", removeBackground: true));

        // Still a redirect — the failure is reported to the operator through
        // TempData rather than as an HTTP error.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var followed = await app.Admin.GetStringAsync(response.Headers.Location!.ToString());
        Assert.Matches(new Regex(
            "enough contrast|plain background|too close in colour",
            RegexOptions.IgnoreCase), followed);
    }

    [Fact]
    public async Task The_frame_page_asks_for_a_picture_when_a_colourway_is_added()
    {
        // The point of the whole change: adding a colourway asks for its
        // photograph there and then, rather than sending staff to the media
        // library and back.
        var (frameId, _) = await AnyVariantAsync();

        var html = await app.Admin.GetStringAsync($"/admin/frames/{frameId}");

        Assert.Contains("name=\"artwork\"", html);
        Assert.Contains("name=\"removeBackground\"", html);
        Assert.Contains("enctype=\"multipart/form-data\"", html);
        Assert.Contains($"/admin/frames/{frameId}/variants", html);
    }

    [Fact]
    public async Task Adding_a_colourway_with_a_photograph_cuts_it_out_and_attaches_it()
    {
        var (frameId, _) = await AnyVariantAsync();

        var token = await app.AntiforgeryTokenAsync(app.Admin, $"/admin/frames/{frameId}");

        var file = new ByteArrayContent(FramePhotograph());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        var colour = $"QA Test {Guid.NewGuid():N}"[..14];
        var body = new MultipartFormDataContent
        {
            { file, "artwork", "colourway.jpg" },
            { new StringContent("true"), "removeBackground" },
            { new StringContent(colour), "ColorName" },
            { new StringContent("0"), "StockQty" },
            { new StringContent(token), "__RequestVerificationToken" },
        };

        var response = await app.Admin.PostAsync($"/admin/frames/{frameId}/variants", body);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var created = await db.FrameVariants.FirstOrDefaultAsync(v => v.ColorName == colour);

        try
        {
            Assert.NotNull(created);

            // Saved in one step: colourway created AND artwork cut out and attached.
            Assert.NotNull(created!.TryOnImageUrl);
            Assert.EndsWith(".png", created.TryOnImageUrl);
        }
        finally
        {
            if (created is not null)
            {
                db.FrameVariants.Remove(created);
                await db.SaveChangesAsync();
            }
        }
    }

    /// <summary>
    /// The frame must be measured from the picture, not left to the fallback.
    ///
    /// DEFAULT_CALIBRATION describes the generated catalogue artwork — a frame
    /// front across 72% of the image with room for the temples either side. A
    /// product photograph is cropped tight, so its front is nearer 90%. The
    /// mirror scales artwork so that the front it believes in matches the face,
    /// so believing the wrong one draws the frame a quarter too large and puts
    /// the temples over the wearer's eyes.
    /// </summary>
    [Fact]
    public async Task Uploaded_artwork_is_calibrated_from_the_picture_itself()
    {
        var (frameId, _) = await AnyVariantAsync();

        var token = await app.AntiforgeryTokenAsync(app.Admin, $"/admin/frames/{frameId}");
        var file = new ByteArrayContent(FramePhotograph());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        var colour = $"Cal {Guid.NewGuid():N}"[..12];
        var body = new MultipartFormDataContent
        {
            { file, "artwork", "measured.jpg" },
            { new StringContent("true"), "removeBackground" },
            { new StringContent(colour), "ColorName" },
            { new StringContent("0"), "StockQty" },
            { new StringContent(token), "__RequestVerificationToken" },
        };

        var response = await app.Admin.PostAsync($"/admin/frames/{frameId}/variants", body);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var created = await db.FrameVariants.FirstOrDefaultAsync(v => v.ColorName == colour);

        try
        {
            Assert.NotNull(created);

            // These four are nullable and are exactly what was being left unset:
            // null here means the mirror falls back to DEFAULT_CALIBRATION.
            Assert.NotNull(created!.TryOnFrontLeftX);
            Assert.NotNull(created.TryOnFrontRightX);
            Assert.NotNull(created.TryOnLensTopY);
            Assert.NotNull(created.TryOnLensBottomY);

            // The lens centres straddle the middle, left before right, and are
            // no longer the 0.29/0.71 the entity defaults to.
            Assert.True(created.AnchorLeftX < created.AnchorRightX);
            Assert.InRange((created.AnchorLeftX + created.AnchorRightX) / 2, 0.45, 0.55);
            Assert.NotEqual(0.29, created.AnchorLeftX, 3);

            // FramePhotograph draws the rims from x=60 to x=420 of 480, so the
            // true frame front is 0.125..0.875. The measurement has to find that
            // and not the bounding box, which the temple stub at x=430 would
            // widen. Accuracy is the claim, so assert against the drawing.
            Assert.InRange(created.TryOnFrontLeftX!.Value, 0.105, 0.145);
            Assert.InRange(created.TryOnFrontRightX!.Value, 0.855, 0.895);

            // And the lens openings, drawn at y=95..155 of 240 → 0.396..0.646.
            Assert.InRange(created.TryOnLensTopY!.Value, 0.37, 0.42);
            Assert.InRange(created.TryOnLensBottomY!.Value, 0.62, 0.67);
        }
        finally
        {
            if (created is not null)
            {
                db.FrameVariants.Remove(created);
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task A_frame_photographed_at_an_angle_is_refused_with_a_reason()
    {
        var (frameId, variantId) = await AnyVariantAsync();

        // A three-quarter view: far lens smaller and higher, near temple showing.
        using var bitmap = new SKBitmap(
            new SKImageInfo(480, 240, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var rim = new SKPaint { Color = SKColors.Black };
            using var hole = new SKPaint { Color = SKColors.White };

            canvas.DrawRect(new SKRect(50, 90, 190, 180), rim);
            canvas.DrawRect(new SKRect(215, 76, 310, 156), rim);
            canvas.DrawRect(new SKRect(310, 98, 430, 118), rim);
            canvas.DrawRect(new SKRect(65, 105, 175, 165), hole);
            canvas.DrawRect(new SKRect(228, 89, 297, 143), hole);
            canvas.Flush();
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 95);

        var page = $"/admin/frames/{frameId}/variants/{variantId}/calibrate";
        var response = await app.Admin.PostAsync(
            $"/admin/frames/{frameId}/variants/{variantId}/artwork",
            await BodyAsync(app.Admin, page, data.ToArray(), "angled.jpg",
                "image/jpeg", removeBackground: true));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var followed = await app.Admin.GetStringAsync(response.Headers.Location!.ToString());
        Assert.Matches(new Regex("at an angle|front-on", RegexOptions.IgnoreCase), followed);
    }

    /// <summary>
    /// The blank-screen bug.
    ///
    /// Uploading a batch of pictures exhausts the upload rate limit, and a 429
    /// carries no body of its own. Status pages used to run in Production only,
    /// so in Development the browser was handed a status and nothing to render:
    /// a completely blank page, no console error, and the developer exception
    /// page silent because a rate limit is not an exception.
    ///
    /// Driven through the sign-in limiter rather than the upload one. It is the
    /// same mechanism and the same empty 429, but it costs eight requests
    /// instead of a hundred and twenty, and it does not spend the shared staff
    /// account's upload budget on a test.
    /// </summary>
    [Fact]
    public async Task A_rate_limited_request_explains_itself_instead_of_going_blank()
    {
        // An address of this test's own, so the limiter's budget is not shared
        // with whatever else the suite is doing.
        const string ip = "203.0.113.42";

        HttpResponseMessage? limited = null;

        for (var attempt = 0; attempt < 14 && limited is null; attempt++)
        {
            using var client = app.CreateClient(
                new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false,
                });

            var response = await app.SignInAsync(
                client, "nobody@example.com", "WrongPassword!1", ip);

            if (response.StatusCode == HttpStatusCode.TooManyRequests) limited = response;
        }

        Assert.NotNull(limited);

        var body = await limited!.Content.ReadAsStringAsync();

        // The point of the test: something to read, not an empty response.
        Assert.NotEmpty(body);
        Assert.Matches(new Regex("too many|wait a few minutes", RegexOptions.IgnoreCase), body);
        Assert.Contains("Nothing was lost", body);
    }

    [Fact]
    public async Task Uploading_artwork_requires_a_staff_account()
    {
        var (frameId, variantId) = await AnyVariantAsync();
        var url = $"/admin/frames/{frameId}/variants/{variantId}/artwork";

        var anonymous = await app.Anonymous.PostAsync(
            url, await BodyAsync(app.Anonymous, "/login", FramePhotograph(),
                "a.jpg", "image/jpeg", true));
        var customer = await app.Customer.PostAsync(
            url, await BodyAsync(app.Customer, "/account", FramePhotograph(),
                "a.jpg", "image/jpeg", true));

        Assert.NotEqual(HttpStatusCode.OK, anonymous.StatusCode);
        Assert.True(
            customer.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect,
            $"a customer got {customer.StatusCode}");
    }
}
