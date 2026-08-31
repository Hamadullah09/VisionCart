using System.Text.RegularExpressions;

namespace VisionCart.IntegrationTests.Http;

/// <summary>
/// Temporary: renders back-office pages to wwwroot/_qa so they can be looked at
/// in a browser during a redesign. Deleted before the branch lands.
/// </summary>
[Collection("http")]
public class QaDump(VisionCartApp app)
{
    [Fact(Skip = "visual QA only — run by hand during a redesign")]
    public async Task Dump()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "VisionCart.Web", "wwwroot", "_qa");
        Directory.CreateDirectory(root);

        var list = await app.Admin.GetStringAsync("/admin/frames");
        await File.WriteAllTextAsync(Path.Combine(root, "frames.html"), list);

        var frameId = Regex.Match(list, @"/admin/frames/([a-z0-9]{20,})").Groups[1].Value;
        var edit = await app.Admin.GetStringAsync($"/admin/frames/{frameId}");
        await File.WriteAllTextAsync(Path.Combine(root, "frame.html"), edit);

        var variantId = Regex
            .Match(edit, $@"/admin/frames/{frameId}/variants/([a-z0-9]+)/calibrate").Groups[1].Value;
        var cal = await app.Admin.GetStringAsync(
            $"/admin/frames/{frameId}/variants/{variantId}/calibrate");
        await File.WriteAllTextAsync(Path.Combine(root, "calibrate.html"), cal);
    }
}
