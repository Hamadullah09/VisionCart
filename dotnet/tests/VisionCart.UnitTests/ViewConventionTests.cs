using System.Text.RegularExpressions;

namespace VisionCart.UnitTests;

/// <summary>
/// Guards against a defect no service-level test can see.
///
/// The media uploader originally received its antiforgery token as
/// <c>data-token="@Html.AntiForgeryToken().ToString()"</c>. That compiles, renders
/// and looks right, but <see cref="Microsoft.AspNetCore.Html.IHtmlContent"/> has no
/// meaningful <c>ToString()</c> — the attribute ended up holding the *type name*, so
/// the token was empty and every upload was rejected with a 400. Nothing failed: the
/// integration tests call the services directly and never cross the HTTP boundary.
///
/// This is a lint, not a behavioural test. It is here because the failure mode is
/// silent, and a reviewer reading the Razor cannot tell the difference by eye.
/// </summary>
public class ViewConventionTests
{
    private static readonly Regex TokenToString =
        new(@"AntiForgeryToken\s*\(\s*\)\s*\.\s*ToString", RegexOptions.IgnoreCase);

    private static readonly Regex TokenInAttribute =
        new(@"=\s*""[^""]*@Html\.AntiForgeryToken", RegexOptions.IgnoreCase);

    public static TheoryData<string> Views()
    {
        var data = new TheoryData<string>();
        foreach (var view in Directory.EnumerateFiles(WebRoot(), "*.cshtml", SearchOption.AllDirectories))
            data.Add(view);
        return data;
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void A_view_never_stringifies_an_antiforgery_token(string path)
    {
        var razor = File.ReadAllText(path);
        var name = Path.GetFileName(path);

        Assert.False(TokenToString.IsMatch(razor),
            $"{name}: AntiForgeryToken().ToString() yields the type name, not the token. " +
            "Render @Html.AntiForgeryToken() as a real hidden field and read it from the DOM.");

        Assert.False(TokenInAttribute.IsMatch(razor),
            $"{name}: an antiforgery token belongs in a hidden field, not an HTML attribute — " +
            "the markup it emits cannot survive being quoted inside one.");
    }

    [Fact]
    public void The_view_folder_was_actually_found()
    {
        // Without this, a wrong path would make every case above vacuously pass.
        Assert.NotEmpty(Views());
    }

    /// <summary>Walks up to the solution folder, then down to the web project's views.</summary>
    private static string WebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("VisionCart.sln*").Any())
            dir = dir.Parent;

        Assert.NotNull(dir);
        var views = Path.Combine(dir!.FullName, "src", "VisionCart.Web");
        Assert.True(Directory.Exists(views), $"expected the web project at {views}");
        return views;
    }
}
