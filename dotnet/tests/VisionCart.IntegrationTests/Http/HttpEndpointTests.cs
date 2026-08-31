using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using VisionCart.Infrastructure.Persistence;

namespace VisionCart.IntegrationTests.Http;

/// <summary>
/// Drives the real application over HTTP: routing, model binding, antiforgery,
/// the authorisation policies, the response headers and the static assets the
/// try-on studio needs.
///
/// None of this is reachable from a service-level test, which is precisely why
/// two real defects survived a green suite.
/// </summary>
[Collection("http")]
public class HttpAuthorizationTests(VisionCartApp app)
{
    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/orders")]
    [InlineData("/admin/patients")]
    [InlineData("/admin/media")]
    [InlineData("/admin/import")]
    [InlineData("/admin/settings")]
    public async Task The_back_office_is_closed_to_anonymous_visitors(string path)
    {
        var response = await app.Anonymous.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.OriginalString ?? "");
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/patients")]
    [InlineData("/admin/media")]
    public async Task A_customer_cannot_reach_the_back_office(string path)
    {
        // Signed in, but with no staff role — the interesting case, because it is
        // authentication succeeding and authorisation having to refuse.
        var response = await app.Customer.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/error/403", response.Headers.Location?.OriginalString ?? "");
    }

    [Fact]
    public async Task Staff_can_reach_the_back_office()
    {
        var response = await app.Staff.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Staff_cannot_reach_the_audit_trail_but_an_administrator_can()
    {
        // §9 of the brief: staff must not reach admin-only functionality. The two
        // halves belong in one test — "staff are refused" is only meaningful
        // alongside proof the page exists and works for someone.
        var refused = await app.Staff.GetAsync("/admin/audit");
        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        Assert.Contains("/error/403", refused.Headers.Location?.OriginalString ?? "");

        var allowed = await app.Admin.GetAsync("/admin/audit");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Theory]
    [InlineData("patients")]
    [InlineData("prescriptions")]
    public async Task Clinical_exports_are_not_reachable_without_signing_in(string dataset)
    {
        var response = await app.Anonymous.GetAsync($"/admin/import/export?type={dataset}");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual("text/csv", response.Content.Headers.ContentType?.MediaType);
    }
}

[Collection("http")]
public class HttpAntiforgeryTests(VisionCartApp app)
{
    [Theory]
    [InlineData("/admin/media/upload")]
    [InlineData("/admin/import/run")]
    public async Task A_post_without_a_token_is_rejected(string path)
    {
        var response = await VisionCartApp.PostFormAsync(
            app.Admin, path, new Dictionary<string, string> { ["kind"] = "frames" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/admin/media")]
    [InlineData("/admin/import")]
    [InlineData("/admin/settings")]
    public async Task Every_page_that_posts_actually_renders_a_token(string path)
    {
        // The defect this whole harness exists for: the media uploader rendered
        // data-token="@Html.AntiForgeryToken().ToString()", which yields the *type
        // name*. The token was empty, every upload came back 400, and the
        // service-level tests never noticed because they do not speak HTTP.
        var token = await app.AntiforgeryTokenAsync(app.Admin, path);

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.DoesNotContain("Microsoft.AspNetCore", token);
        Assert.True(token.Length > 20, $"{path} rendered a suspiciously short token.");
    }

    [Fact]
    public async Task The_sign_in_form_renders_a_token_too()
    {
        // Anonymous, because /login redirects anyone already signed in.
        var token = await app.AntiforgeryTokenAsync(app.Anonymous, "/login");

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.True(token.Length > 20);
    }

    [Fact]
    public async Task An_upload_with_a_token_reaches_the_image_pipeline()
    {
        var token = await app.AntiforgeryTokenAsync(app.Admin, "/admin/media");

        using var content = new MultipartFormDataContent();
        var png = new ByteArrayContent(PngBytes());
        png.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(png, "file", "http-harness.png");
        content.Add(new StringContent("http-harness"), "tags");
        content.Add(new StringContent("false"), "keepAlpha");
        content.Add(new StringContent(token), "__RequestVerificationToken");

        var response = await app.Admin.PostAsync("/admin/media/upload", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"ok\":true", body);

        // Prove it is really in the library, then take it back out again: this
        // suite runs against a database a developer also browses.
        var library = await app.Admin.GetStringAsync("/admin/media?q=http-harness");
        Assert.Contains("http-harness", library);

        var id = Regex.Match(library, @"/admin/media/([a-z0-9]+)/delete").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(id), "the uploaded image had no delete control");

        var deleteToken = await app.AntiforgeryTokenAsync(app.Admin, "/admin/media");
        var deleted = await VisionCartApp.PostFormAsync(
            app.Admin, $"/admin/media/{id}/delete",
            new Dictionary<string, string> { ["__RequestVerificationToken"] = deleteToken });

        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
    }

    private static byte[] PngBytes()
    {
        using var bitmap = new SKBitmap(120, 80);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(new SKColor(0x0b, 0x5f, 0xa5));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}

[Collection("http")]
public class HttpAssetTests(VisionCartApp app)
{
    [Theory]
    [InlineData("/models/face_landmarker.task", "application/octet-stream")]
    [InlineData("/js/vendor/mediapipe-vision.mjs", "text/javascript")]
    public async Task The_try_on_assets_are_served_with_a_type_the_browser_accepts(
        string path, string expectedType)
    {
        // ASP.NET Core refuses to serve a file whose extension it does not know,
        // so .task and .wasm 404 until they are registered. That failure is silent
        // on the server and fatal in the browser: the mirror simply never starts.
        var response = await app.Anonymous.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedType, response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Content.Headers.ContentLength > 0);
    }

    [Theory]
    [InlineData("/wasm/vision_wasm_internal.wasm")]
    [InlineData("/wasm/vision_wasm_nosimd_internal.wasm")]
    public async Task The_wasm_runtime_is_served(string path)
    {
        var response = await app.Anonymous.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/wasm", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Content.Headers.ContentLength > 0);
    }

    [Fact]
    public async Task Security_headers_are_present_on_every_response()
    {
        var response = await app.Anonymous.GetAsync("/");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task The_content_security_policy_names_no_external_origin()
    {
        // §7: face processing happens in the browser and nothing leaves it. A CSP
        // that permitted a CDN would let a future edit quietly reintroduce one.
        var response = await app.Anonymous.GetAsync("/try-on");
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();

        Assert.DoesNotContain("http://", csp.Replace("http://localhost", ""));
        Assert.DoesNotContain("https://", csp);
        Assert.Contains("wasm-unsafe-eval", csp);
    }
}

[Collection("http")]
public class HttpPrivacyTests(VisionCartApp app)
{
    private static readonly Regex Email = new(@"[\w.+-]+@[\w-]+\.[\w.]+", RegexOptions.Compiled);

    [Fact]
    public async Task No_patient_link_carries_clinical_data_in_its_url()
    {
        // §10: patient and prescription data must never appear in URLs, because a
        // URL ends up in browser history, proxy logs and the Referer header.
        var html = await app.Admin.GetStringAsync("/admin/patients");

        var links = Regex.Matches(html, @"href=""(/admin/patients[^""]*)""")
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
            .ToList();

        Assert.NotEmpty(links);

        foreach (var link in links)
        {
            Assert.DoesNotMatch(Email, link);
            Assert.DoesNotContain("sphere", link, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cylinder", link, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("axis", link, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("dob", link, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_missing_page_does_not_leak_a_stack_trace()
    {
        var response = await app.Anonymous.GetAsync("/admin/patients/not-a-real-id");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("VisionCart.Application", body);
        Assert.DoesNotContain("at Microsoft.", body);
        Assert.DoesNotContain("Stack trace", body, StringComparison.OrdinalIgnoreCase);
    }
}

[Collection("http")]
public class HttpRateLimitTests(VisionCartApp app)
{
    [Fact]
    public async Task Repeated_sign_in_attempts_are_throttled()
    {
        const string ip = "203.0.113.10";
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 12; attempt++)
        {
            using var client = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing
                .WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await app.SignInAsync(
                client, "nobody@example.com", "WrongPassword!1", ip);
            statuses.Add(response.StatusCode);

            if (response.StatusCode == HttpStatusCode.TooManyRequests) break;
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task One_visitor_cannot_throttle_everybody_else()
    {
        // A sign-in limiter with a single global bucket is a denial-of-service
        // vector: eight bad attempts from anyone locks every other customer out
        // of the shop for five minutes. The budget must be per client.
        const string attacker = "203.0.113.20";
        const string bystander = "203.0.113.21";

        for (var attempt = 0; attempt < 12; attempt++)
        {
            using var burner = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing
                .WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await app.SignInAsync(burner, "nobody@example.com", "WrongPassword!1", attacker);
        }

        using var innocent = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing
            .WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await app.SignInAsync(
            innocent, "someone-else@example.com", "WrongPassword!1", bystander);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task A_failed_sign_in_does_not_reveal_whether_the_account_exists()
    {
        const string ip = "203.0.113.30";

        using var a = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing
            .WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var unknown = await app.SignInAsync(a, "no-such-person@example.com", "Wrong!12345", ip);

        using var b = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing
            .WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var realAccount = await app.SignInAsync(b, app.CustomerEmail, "Wrong!12345", ip);

        Assert.Equal(unknown.StatusCode, realAccount.StatusCode);

        // Compare the message the visitor is shown, not the whole page: the form
        // legitimately echoes back the address that was typed, and the antiforgery
        // token is regenerated per request. Neither of those tells an attacker
        // anything. The wording of the refusal would.
        var unknownMessage = ErrorMessage(await unknown.Content.ReadAsStringAsync());
        var realMessage = ErrorMessage(await realAccount.Content.ReadAsStringAsync());

        Assert.False(string.IsNullOrWhiteSpace(unknownMessage), "no refusal was shown");
        Assert.Equal(unknownMessage, realMessage);
    }

    /// <summary>Pulls the validation summary out of the rendered sign-in form.</summary>
    private static string ErrorMessage(string html)
    {
        var summary = Regex.Match(html, """<div[^>]*class="[^"]*alert-error[^"]*"[^>]*>(.*?)</div>""",
            RegexOptions.Singleline);

        return Regex.Replace(
            WebUtility.HtmlDecode(Regex.Replace(summary.Groups[1].Value, "<[^>]+>", " ")),
            @"\s+", " ").Trim();
    }

}

/// <summary>
/// The try-on calibration screen, over HTTP.
///
/// The mirror draws a frame at its recorded width in millimetres, and it can
/// only do that if somebody has marked up where the frame is inside its own
/// picture. That marking-up happens here, so a route that has quietly stopped
/// rendering — or a form whose field names no longer bind — takes the accuracy
/// of every frame in the shop with it.
/// </summary>
[Collection("http")]
public class HttpCalibrationTests(VisionCartApp app)
{
    private async Task<(string FrameId, string VariantId)> AnyCalibratableAsync()
    {
        var list = await app.Admin.GetStringAsync("/admin/frames");

        // At least 20 characters, so "/admin/frames/new" from the toolbar does
        // not get mistaken for a frame.
        var frameId = Regex.Match(list, @"/admin/frames/([a-z0-9]{20,})").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(frameId), "the frame list showed no frames");

        var edit = await app.Admin.GetStringAsync($"/admin/frames/{frameId}");
        var variantId = Regex
            .Match(edit, $@"/admin/frames/{frameId}/variants/([a-z0-9]+)/calibrate")
            .Groups[1].Value;

        Assert.False(string.IsNullOrEmpty(variantId),
            "the frame page offered no route to the calibration screen");

        return (frameId, variantId);
    }

    [Fact]
    public async Task The_frame_page_sends_staff_to_the_calibration_screen_rather_than_a_grid_of_numbers()
    {
        var (frameId, _) = await AnyCalibratableAsync();
        var edit = await app.Admin.GetStringAsync($"/admin/frames/{frameId}");

        Assert.Contains("Calibrate the artwork", edit);

        // The old screen asked an administrator to type fractions of an image
        // into text boxes. Nobody can do that accurately, and the frame is
        // drawn wrong for every customer when they get it wrong.
        Assert.DoesNotContain("name=\"anchorLeftX\"", edit);
        Assert.DoesNotContain("name=\"anchorRightY\"", edit);
    }

    [Fact]
    public async Task The_calibration_screen_renders_its_markers_and_its_configuration()
    {
        var (frameId, variantId) = await AnyCalibratableAsync();
        var html = await app.Admin.GetStringAsync(
            $"/admin/frames/{frameId}/variants/{variantId}/calibrate");

        foreach (var marker in new[]
                 {
                     "leftLensCenter", "rightLensCenter",
                     "frontLeftX", "frontRightX", "lensTopY", "lensBottomY",
                 })
        {
            Assert.Contains($"data-cal-marker=\"{marker}\"", html);
        }

        // The client needs the millimetres to check the marks against; without
        // them the screen still drags but can no longer tell anyone it is wrong.
        Assert.Contains("id=\"calibrate-config\"", html);
        Assert.Contains("lensWidthMm", html);
        Assert.Contains("totalWidthMm", html);

        // Every marker is a real control, so the screen works from a keyboard.
        Assert.Contains("aria-label=\"Centre of the left lens\"", html);
    }

    [Fact]
    public async Task Saving_a_calibration_round_trips_through_the_form()
    {
        var (frameId, variantId) = await AnyCalibratableAsync();
        var path = $"/admin/frames/{frameId}/variants/{variantId}/calibrate";
        var token = await app.AntiforgeryTokenAsync(app.Admin, path);

        Assert.False(string.IsNullOrWhiteSpace(token), "the calibration form rendered no token");

        var before = await app.Admin.GetStringAsync(path);
        var originals = Regex.Matches(before, @"data-cal-value=""(\w+)""")
            .Select(m => m.Groups[1].Value).ToList();

        Assert.Contains("frontLeftX", originals);

        // Put back whatever was there. This suite runs against a database a
        // developer also browses, and a frame left miscalibrated by a test is
        // one somebody else spends an afternoon investigating.
        var original = Regex.Matches(before, @"data-cal-value=""(\w+)""[^>]*value=""([\d.]+)""")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

        var response = await VisionCartApp.PostFormAsync(app.Admin, path, new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["LeftLensCenterX"] = "0.3100", ["LeftLensCenterY"] = "0.5100",
            ["RightLensCenterX"] = "0.6900", ["RightLensCenterY"] = "0.5100",
            ["FrontLeftX"] = "0.1200", ["FrontRightX"] = "0.8800",
            ["LensTopY"] = "0.1500", ["LensBottomY"] = "0.8500",
            ["Opacity"] = "0.90",
            ["ImageWidth"] = "1330", ["ImageHeight"] = "413",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var after = await app.Admin.GetStringAsync(path);
        Assert.Contains("\"frontLeftX\":0.12", after);
        Assert.Contains("\"lensBottomY\":0.85", after);

        if (original.Count == 8)
        {
            var restoreToken = await app.AntiforgeryTokenAsync(app.Admin, path);
            var restore = new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = restoreToken,
                ["LeftLensCenterX"] = original["leftLensCenterX"],
                ["LeftLensCenterY"] = original["leftLensCenterY"],
                ["RightLensCenterX"] = original["rightLensCenterX"],
                ["RightLensCenterY"] = original["rightLensCenterY"],
                ["FrontLeftX"] = original["frontLeftX"],
                ["FrontRightX"] = original["frontRightX"],
                ["LensTopY"] = original["lensTopY"],
                ["LensBottomY"] = original["lensBottomY"],
                ["Opacity"] = "1",
                ["ImageWidth"] = original.GetValueOrDefault("imageWidth", ""),
                ["ImageHeight"] = original.GetValueOrDefault("imageHeight", ""),
            };
            await VisionCartApp.PostFormAsync(app.Admin, path, restore);
        }
    }

    [Fact]
    public async Task A_calibration_the_mirror_could_not_draw_from_is_refused()
    {
        var (frameId, variantId) = await AnyCalibratableAsync();
        var path = $"/admin/frames/{frameId}/variants/{variantId}/calibrate";
        var token = await app.AntiforgeryTokenAsync(app.Admin, path);

        var response = await VisionCartApp.PostFormAsync(app.Admin, path, new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            // Lens centres outside the frame front: the picture contradicts itself.
            ["LeftLensCenterX"] = "0.0500", ["LeftLensCenterY"] = "0.5000",
            ["RightLensCenterX"] = "0.9500", ["RightLensCenterY"] = "0.5000",
            ["FrontLeftX"] = "0.2000", ["FrontRightX"] = "0.8000",
            ["LensTopY"] = "0.1500", ["LensBottomY"] = "0.8500",
            ["Opacity"] = "1",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var after = await app.Admin.GetStringAsync(path);
        Assert.Contains("must sit inside the frame front", after);
        // And the bad values must not have been written.
        Assert.DoesNotContain("\"leftLensCenterX\":0.05", after);
    }

    [Fact]
    public async Task The_frame_list_says_which_frames_cannot_be_tried_on()
    {
        var html = await app.Admin.GetStringAsync("/admin/frames");

        // Silence here means an administrator only discovers a broken frame by
        // opening the mirror and looking at it.
        Assert.Contains("tryon-state", html);
        Assert.Matches(@"tryon-state is-(ready|warning|blocked)", html);
    }

    [Fact]
    public async Task Anonymous_visitors_cannot_reach_the_calibration_screen()
    {
        var (frameId, variantId) = await AnyCalibratableAsync();
        var response = await app.Anonymous.GetAsync(
            $"/admin/frames/{frameId}/variants/{variantId}/calibrate");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.OriginalString ?? "");
    }
}

/// <summary>
/// Signing out, over HTTP.
///
/// The sign-out button returned 400 from every page in the application: the
/// form wrote <c>action="/logout"</c> by hand, which turns the form tag
/// helper's antiforgery default off, so no token was rendered and the global
/// filter rejected the post. Everything looked right — the form was there, the
/// button was there — and nothing in the suite crossed that route.
/// </summary>
[Collection("http")]
public class HttpSignOutTests(VisionCartApp app)
{
    [Fact]
    public async Task Every_page_offering_sign_out_renders_a_token_for_it()
    {
        foreach (var path in new[] { "/account", "/admin" })
        {
            var html = await app.Admin.GetStringAsync(path);

            Assert.Contains("/logout", html);
            Assert.Matches(
                @"<form[^>]*action=""/logout""[\s\S]{0,400}?__RequestVerificationToken",
                html);
        }
    }

    [Fact]
    public async Task A_signed_in_customer_can_sign_out()
    {
        var client = await app.NewSignedInCustomerAsync();

        var before = await client.GetAsync("/account");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        var token = await app.AntiforgeryTokenAsync(client, "/account");
        Assert.False(string.IsNullOrWhiteSpace(token), "/account rendered no antiforgery token");

        var signedOut = await VisionCartApp.PostFormAsync(client, "/logout",
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token });

        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.Equal("/", signedOut.Headers.Location?.OriginalString);

        // And the session is really gone, not merely redirected away from.
        var after = await client.GetAsync("/account");
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.Contains("/login", after.Headers.Location?.OriginalString ?? "");
    }

    [Fact]
    public async Task Typing_the_logout_url_does_not_sign_anyone_out()
    {
        // A GET must never change state — otherwise any page on the internet
        // could sign our customers out with an image tag.
        var response = await app.Admin.GetAsync("/logout");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var still = await app.Admin.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
    }

    [Fact]
    public async Task A_sign_out_without_a_token_is_refused()
    {
        var response = await VisionCartApp.PostFormAsync(
            app.Admin, "/logout", new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

/// <summary>
/// The product workspace and the stock tabs, over HTTP.
///
/// A back office is judged on whether the answer it gives is the true one, so
/// these check the numbers rather than the markup: that a stock tab really
/// narrows the list, that a vendor's frame count is not quietly zero, and that
/// everything the practice records about a frame reaches one page.
/// </summary>
[Collection("http")]
public class HttpProductWorkspaceTests(VisionCartApp app)
{
    private static int RowCount(string html)
    {
        var body = html.Split("<tbody>").ElementAtOrDefault(1)?.Split("</tbody>")[0] ?? "";
        return Regex.Matches(body, "<tr").Count;
    }

    private async Task<string> AnyFrameIdAsync()
    {
        var list = await app.Admin.GetStringAsync("/admin/frames");
        var id = Regex.Match(list, @"/admin/frames/([a-z0-9]{20,})").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(id), "the frame list showed no frames");
        return id;
    }

    [Fact]
    public async Task The_stock_tabs_actually_narrow_the_list()
    {
        var all = RowCount(await app.Admin.GetStringAsync("/admin/frames"));
        var inStock = RowCount(await app.Admin.GetStringAsync("/admin/frames?stock=in"));
        var outOfStock = RowCount(await app.Admin.GetStringAsync("/admin/frames?stock=out"));

        Assert.True(all > 0, "no frames at all");

        // Every frame is either sellable or it is not, so the two halves have to
        // add up. A filter that quietly matched everything would pass a
        // "returns rows" assertion and fail this one.
        Assert.Equal(all, inStock + outOfStock);
        Assert.True(inStock > 0, "nothing is in stock, which cannot be right");

        // Both sides have to be non-empty for this to prove anything: a filter
        // that matched everything would still satisfy the sum above.
        Assert.True(outOfStock > 0,
            "no frame is out of stock, so the filter has nothing to demonstrate");
        Assert.True(inStock < all, "the in-stock filter did not narrow the list at all");
    }

    [Fact]
    public async Task A_stock_tab_keeps_the_search_that_is_already_applied()
    {
        var html = await app.Admin.GetStringAsync("/admin/frames?stock=in&q=ravi");

        Assert.Contains("value=\"ravi\"", html);
        Assert.Contains("stock=in", html);
        Assert.Equal(1, RowCount(html));
    }

    [Fact]
    public async Task Everything_the_practice_records_about_a_frame_is_on_one_page()
    {
        var html = await app.Admin.GetStringAsync($"/admin/frames/{await AnyFrameIdAsync()}/detail");

        // The sections a dispenser or a buyer actually needs, all reachable
        // without opening a modal that shows half of them.
        foreach (var heading in new[]
                 {
                     "Overview", "Frame", "Lens", "Fitting features",
                     "Colourways, stock and where to find them",
                     "Price and cost", "Vendor", "Images",
                 })
        {
            Assert.Contains(heading, html);
        }

        // And the figures themselves, not just the headings above them.
        Assert.Contains("Lens centres", html);
        Assert.Contains("Their code for this", html);
        Assert.Contains("Lead time", html);
        Assert.Matches(@"Aisle\s*\d+", html);
    }

    [Fact]
    public async Task The_lens_centre_distance_is_the_lens_plus_the_bridge()
    {
        // Not a stored column — computed, and the number the try-on works
        // decentration out from. Worth pinning where a reader can see it.
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var frame = await db.Frames.AsNoTracking()
            .FirstAsync(f => f.LensWidthMm != null && f.BridgeWidthMm != null);

        var html = await app.Admin.GetStringAsync($"/admin/frames/{frame.Id}/detail");
        var expected = frame.LensWidthMm!.Value + frame.BridgeWidthMm!.Value;

        Assert.Contains($"{expected:0.#} mm", html);
    }

    [Fact]
    public async Task The_vendor_list_counts_the_frames_it_supplies()
    {
        var html = await app.Admin.GetStringAsync("/admin/vendors");

        Assert.Contains("Opticore", html);

        // The defect this catches: the count was read off a navigation property
        // the query never loaded, so every vendor reported zero frames while
        // the page looked entirely correct.
        var counts = Regex.Matches(html, @"<td class=""num"">(\d+)</td>")
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToList();

        Assert.NotEmpty(counts);
        Assert.True(counts.Any(c => c > 0),
            "every vendor reported zero frames, which means the count is not being loaded");
    }

    [Fact]
    public async Task A_vendor_can_be_saved_and_read_back()
    {
        var token = await app.AntiforgeryTokenAsync(app.Admin, "/admin/vendors/new");

        var created = await VisionCartApp.PostFormAsync(app.Admin, "/admin/vendors/save",
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Name"] = "ZZ Test Optical",
                ["Code"] = "zz-test",
                ["ContactName"] = "Trade desk",
                ["LeadTimeDays"] = "4",
                ["IsActive"] = "true",
            });

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        var list = await app.Admin.GetStringAsync("/admin/vendors");
        Assert.Contains("ZZ Test Optical", list);

        // A duplicate code must be refused, or two vendors answer to one account.
        var duplicateToken = await app.AntiforgeryTokenAsync(app.Admin, "/admin/vendors/new");
        var duplicate = await VisionCartApp.PostFormAsync(app.Admin, "/admin/vendors/save",
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = duplicateToken,
                ["Name"] = "ZZ Another Optical",
                ["Code"] = "zz-test",
                ["IsActive"] = "true",
            });

        Assert.Equal(HttpStatusCode.Redirect, duplicate.StatusCode);
        Assert.DoesNotContain("ZZ Another Optical", await app.Admin.GetStringAsync("/admin/vendors"));

        // Tidy up: this suite runs against a database a developer also browses.
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stray = await db.Vendors.Where(v => v.Name.StartsWith("ZZ ")).ToListAsync();
        db.Vendors.RemoveRange(stray);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Anonymous_visitors_cannot_reach_the_workspace_or_the_vendors()
    {
        foreach (var path in new[] { "/admin/vendors", $"/admin/frames/{await AnyFrameIdAsync()}/detail" })
        {
            var response = await app.Anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/login", response.Headers.Location?.OriginalString ?? "");
        }
    }
}
