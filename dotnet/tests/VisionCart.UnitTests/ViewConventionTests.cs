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

    /// <summary>
    /// A POST form whose <c>action</c> is written out literally and which
    /// carries no <c>asp-</c> attribute — neither routing to replace it nor
    /// <c>asp-antiforgery</c> to say the omission was deliberate.
    /// </summary>
    private static readonly Regex PostFormWithLiteralAction =
        new("""<form\b(?=[^>]*\bmethod\s*=\s*["']post["'])(?=[^>]*\baction\s*=)(?![^>]*\basp-)[^>]*>""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

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

    /// <summary>
    /// The sign-out button returned 400 from every page in the application.
    ///
    /// Writing <c>action="/logout"</c> by hand turns the form tag helper's
    /// antiforgery default OFF — the helper only injects a token when it
    /// generates the action itself. The form rendered, the button looked
    /// right, and the global AutoValidateAntiforgeryToken filter rejected the
    /// post. Nothing failed anywhere: no test crossed the HTTP boundary on
    /// that route, and a reviewer reading the Razor cannot see it.
    ///
    /// Route the form instead of hard-coding its target, or say
    /// <c>asp-antiforgery="true"</c> and mean it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Views))]
    public void A_post_form_never_hard_codes_its_action(string path)
    {
        var razor = File.ReadAllText(path);
        var match = PostFormWithLiteralAction.Match(razor);

        Assert.False(match.Success,
            $"{Path.GetFileName(path)}: {match.Value.Trim()} — a literal action on a POST form "
            + "suppresses the antiforgery token, so the post comes back 400. Use asp-controller "
            + "and asp-action, or set asp-antiforgery=\"true\".");
    }

    /// <summary>
    /// The lint above only earns its place if it fires. A regex that silently
    /// stops matching is worse than no lint at all, because the suite still
    /// goes green — which is exactly how the defect it guards reached the user.
    /// </summary>
    [Fact]
    public void The_hard_coded_action_lint_actually_fires()
    {
        Assert.True(
            PostFormWithLiteralAction.IsMatch("""<form method="post" action="/logout" style="display:inline;">"""),
            "the lint no longer recognises a hard-coded action on a POST form");

        Assert.False(
            PostFormWithLiteralAction.IsMatch("""<form method="post" asp-controller="Account" asp-action="Logout">"""),
            "the lint rejects a correctly routed form");

        Assert.False(
            PostFormWithLiteralAction.IsMatch("""<form method="get" action="/frames">"""),
            "a GET form carries no token and needs none");
    }
}
