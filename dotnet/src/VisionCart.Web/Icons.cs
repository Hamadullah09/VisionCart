using Microsoft.AspNetCore.Html;

namespace VisionCart.Web;

/// <summary>
/// The back-office icon set.
///
/// Inline SVG rather than an icon font or a sprite sheet: the
/// Content-Security-Policy names no external origin, an icon font costs a
/// render-blocking download for two dozen glyphs, and drawing in
/// <c>currentColor</c> means an icon inherits the colour of whatever it sits
/// in — which is what makes the navigation's active state carry its icon with
/// it without a second rule.
///
/// One 24px grid, one stroke weight. Anything added here must match, or the
/// set stops looking like a set.
/// </summary>
public static class Icons
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        // Navigation
        ["dashboard"] = "<rect x='3' y='3' width='7' height='7' rx='1.5'/><rect x='14' y='3' width='7' height='7' rx='1.5'/><rect x='3' y='14' width='7' height='7' rx='1.5'/><rect x='14' y='14' width='7' height='7' rx='1.5'/>",
        ["orders"] = "<path d='M6 2 4 6v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V6l-2-4Z'/><path d='M4 6h16'/><path d='M16 10a4 4 0 0 1-8 0'/>",
        ["promos"] = "<path d='M20.6 13.4 12 22l-9-9V4h9l8.6 8.6a2 2 0 0 1 0 2.8Z'/><circle cx='7.5' cy='7.5' r='1.5'/>",
        ["delivery"] = "<path d='M14 17V5a1 1 0 0 0-1-1H2v12h1'/><path d='M14 9h4l3 3v5h-2'/><circle cx='6.5' cy='17.5' r='2.5'/><circle cx='17.5' cy='17.5' r='2.5'/>",
        ["frames"] = "<circle cx='6' cy='14' r='4'/><circle cx='18' cy='14' r='4'/><path d='M10 14h4'/><path d='M2 12l2-4h3'/><path d='M22 12l-2-4h-3'/>",
        ["glasses"] = "<circle cx='6' cy='14' r='3.4'/><circle cx='18' cy='14' r='3.4'/><path d='M9.4 14h5.2'/><path d='M2.6 12l1.8-3.6h2.4'/><path d='M21.4 12l-1.8-3.6h-2.4'/>",
        ["lenses"] = "<circle cx='12' cy='12' r='9'/><circle cx='12' cy='12' r='3.5'/>",
        ["vendors"] = "<path d='M3 21h18'/><path d='M5 21V5a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v16'/><path d='M15 9h4a2 2 0 0 1 2 2v10'/><path d='M9 7h2M9 11h2M9 15h2'/>",
        ["media"] = "<rect x='3' y='3' width='18' height='18' rx='2'/><circle cx='9' cy='9' r='1.6'/><path d='m21 15-5-5L5 21'/>",
        ["patients"] = "<path d='M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2'/><circle cx='9' cy='7' r='4'/><path d='M22 21v-2a4 4 0 0 0-3-3.87'/><path d='M16 3.13a4 4 0 0 1 0 7.75'/>",
        ["diary"] = "<rect x='3' y='4' width='18' height='18' rx='2'/><path d='M16 2v4M8 2v4M3 10h18'/>",
        ["import"] = "<path d='M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4'/><path d='M7 10l5 5 5-5'/><path d='M12 15V3'/>",
        ["privacy"] = "<path d='M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z'/>",
        ["audit"] = "<path d='M3 12a9 9 0 1 0 3-6.7L3 8'/><path d='M3 3v5h5'/><path d='M12 7v5l3 2'/>",
        ["settings"] = "<path d='M4 21v-7M4 10V3M12 21v-9M12 8V3M20 21v-5M20 12V3'/><path d='M1 14h6M9 8h6M17 16h6'/>",

        // Chrome
        ["shop"] = "<path d='M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6'/><path d='M15 3h6v6'/><path d='M10 14 21 3'/>",
        ["search"] = "<circle cx='11' cy='11' r='7'/><path d='m21 21-4.3-4.3'/>",
        ["bell"] = "<path d='M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9'/><path d='M13.7 21a2 2 0 0 1-3.4 0'/>",
        ["chevron"] = "<path d='m6 9 6 6 6-6'/>",
        ["burger"] = "<path d='M3 6h18M3 12h18M3 18h18'/>",
        ["panel"] = "<rect x='3' y='3' width='18' height='18' rx='2'/><path d='M9 3v18'/>",
        ["logout"] = "<path d='M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4'/><path d='m16 17 5-5-5-5'/><path d='M21 12H9'/>",
        ["user"] = "<circle cx='12' cy='8' r='4'/><path d='M4 21a8 8 0 0 1 16 0'/>",
        ["plus"] = "<path d='M12 5v14M5 12h14'/>",

        // Status and measures
        ["money"] = "<rect x='2' y='5' width='20' height='14' rx='2'/><path d='M2 10h20'/>",
        ["card"] = "<rect x='2' y='5' width='20' height='14' rx='2'/><path d='M2 10h20'/><path d='M6 15h4'/>",
        ["clock"] = "<circle cx='12' cy='12' r='9'/><path d='M12 7v5l3 2'/>",
        ["flask"] = "<path d='M9 3v6.5L4.2 18A2 2 0 0 0 6 21h12a2 2 0 0 0 1.8-3L15 9.5V3'/><path d='M8 3h8'/><path d='M7.5 15h9'/>",
        ["box"] = "<path d='M21 8 12 3 3 8v8l9 5 9-5Z'/><path d='m3 8 9 5 9-5'/><path d='M12 13v8'/>",
        ["tick"] = "<path d='M20 6 9 17l-5-5'/>",
        ["warning"] = "<path d='M12 9v4M12 17h.01'/><path d='M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z'/>",
        ["info"] = "<circle cx='12' cy='12' r='9'/><path d='M12 16v-4M12 8h.01'/>",
        ["trend-up"] = "<path d='m3 17 6-6 4 4 8-8'/><path d='M17 7h4v4'/>",
        ["trend-down"] = "<path d='m3 7 6 6 4-4 8 8'/><path d='M17 17h4v-4'/>",
        ["inbox"] = "<path d='M22 12h-6l-2 3h-4l-2-3H2'/><path d='M5.5 5h13l3.5 7v6a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2v-6Z'/>",

        // Storefront
        // A trolley, not a tote: the shop is called VisionCart, the route is
        // /cart and the service is CartService — only the wording in the header
        // ever said "bag".
        ["cart"] = "<circle cx='9' cy='20' r='1.6'/><circle cx='18.5' cy='20' r='1.6'/><path d='M2.5 3h2.2l2.6 11.4a1.8 1.8 0 0 0 1.76 1.4h8.9a1.8 1.8 0 0 0 1.75-1.37L21.5 7H6'/>",
        ["camera"] = "<path d='M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2Z'/><circle cx='12' cy='13' r='4'/>",
        ["shield"] = "<path d='M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z'/><path d='m9 12 2 2 4-4'/>",
        ["truck"] = "<path d='M14 17V5a1 1 0 0 0-1-1H2v12h1'/><path d='M14 9h4l3 3v5h-2'/><circle cx='6.5' cy='17.5' r='2.5'/><circle cx='17.5' cy='17.5' r='2.5'/>",
        ["return"] = "<path d='M3 12a9 9 0 1 0 3-6.7L3 8'/><path d='M3 3v5h5'/>",
        ["ruler"] = "<path d='M16.8 2.3 21.7 7.2a1 1 0 0 1 0 1.4L8.6 21.7a1 1 0 0 1-1.4 0L2.3 16.8a1 1 0 0 1 0-1.4L15.4 2.3a1 1 0 0 1 1.4 0Z'/><path d='m7.5 10.5 2 2M10.5 7.5l2 2M13.5 4.5l2 2M4.5 13.5l2 2'/>",
        ["eye"] = "<path d='M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7-10-7-10-7Z'/><circle cx='12' cy='12' r='3'/>",
        ["arrow-right"] = "<path d='M5 12h14'/><path d='m12 5 7 7-7 7'/>",
        ["spark"] = "<path d='M12 2.5 14.4 9 21 11.5 14.4 14 12 20.5 9.6 14 3 11.5 9.6 9Z'/>",
        ["men"] = "<circle cx='10' cy='14' r='6.5'/><path d='m15 9 6-6'/><path d='M16 3h5v5'/>",
        ["women"] = "<circle cx='12' cy='9' r='6'/><path d='M12 15v7'/><path d='M9 19h6'/>",
        ["kids"] = "<circle cx='12' cy='12' r='9'/><path d='M9 10h.01M15 10h.01'/><path d='M9 15a4 4 0 0 0 6 0'/>",
    };

    /// <summary>
    /// One icon, drawn in <c>currentColor</c>. Unknown names render an empty
    /// SVG rather than throwing: a missing glyph should not take a page down.
    /// </summary>
    public static IHtmlContent Svg(string name, string? cssClass = null)
    {
        var body = Paths.TryGetValue(name, out var d) ? d : string.Empty;
        var css = cssClass is null ? "" : $" class=\"{cssClass}\"";
        return new HtmlString(
            $"<svg{css} viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" " +
            $"stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\">{body}</svg>");
    }
}
