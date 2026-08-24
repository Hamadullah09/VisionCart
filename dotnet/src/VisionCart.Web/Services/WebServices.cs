using System.Security.Claims;
using VisionCart.Application.Carts;
using VisionCart.Application.Platform;

namespace VisionCart.Web.Services;

/// <summary>
/// Supplies the acting user and their client details from the current request.
/// Replaces the legacy <c>getSession()</c> plus the header sniffing that
/// <c>audit.ts</c> did inline.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public string? UserId => IsAuthenticated
        ? Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
        : null;

    public string? Email => IsAuthenticated
        ? Principal?.FindFirstValue(ClaimTypes.Email) ?? Principal?.Identity?.Name
        : null;

    /// <summary>
    /// Behind IIS and any reverse proxy the socket address is the proxy, so the
    /// forwarded header is preferred — matching the legacy behaviour. Forwarded
    /// headers are only trusted because ForwardedHeaders middleware validates
    /// them upstream; see Program.cs.
    /// </summary>
    public string? Ip
    {
        get
        {
            var context = accessor.HttpContext;
            if (context is null) return null;

            var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded))
                return forwarded.Split(',')[0].Trim();

            return context.Request.Headers["X-Real-IP"].FirstOrDefault()
                   ?? context.Connection.RemoteIpAddress?.ToString();
        }
    }

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.FirstOrDefault();

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
}

/// <summary>
/// Reads and writes the cart cookie. The cart service stays free of HTTP; this
/// is the only place the cookie name and its flags are decided.
/// </summary>
public sealed class CookieCartTokenAccessor(IHttpContextAccessor accessor) : ICartTokenAccessor
{
    public const string CookieName = "vc_cart";
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(60);

    public string? Read() => accessor.HttpContext?.Request.Cookies[CookieName];

    public void Write(string token) =>
        accessor.HttpContext?.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            // Secure in production only, so local HTTP development still works.
            Secure = accessor.HttpContext.Request.IsHttps,
            Path = "/",
            MaxAge = MaxAge,
            IsEssential = true,
        });

    public void Clear() => accessor.HttpContext?.Response.Cookies.Delete(CookieName);
}
