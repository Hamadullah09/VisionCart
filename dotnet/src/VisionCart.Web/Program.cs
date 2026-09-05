using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using VisionCart.Application.Carts;
using VisionCart.Application.Platform;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using VisionCart.Infrastructure.Logging;
using VisionCart.Infrastructure.Platform;
using VisionCart.Infrastructure;
using VisionCart.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Kestrel announces itself on every response. Behind IIS the header is replaced
// anyway, but the application should not be the thing volunteering its stack to
// someone fingerprinting the site.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddControllersWithViews(options =>
{
    // Server actions carried CSRF protection implicitly. Making it global and
    // explicit means a new POST cannot be added without it.
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddVisionCartInfrastructure(builder.Configuration);

// HTTP-side implementations of the abstractions the application layer declares.
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ICartTokenAccessor, CookieCartTokenAccessor>();

// --- Authentication ---------------------------------------------------------
// Cookie authentication replaces the hand-rolled jose JWT. Identity's security
// stamp gives what the legacy token could not: server-side revocation, so a
// password change invalidates sessions already issued.
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.Cookie.Name = "vc_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/error/403";
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.StaffOnly,
        policy => policy.RequireRole(Roles.Staff, Roles.Optician, Roles.Admin));
    options.AddPolicy(AuthorizationPolicies.OpticianOnly,
        policy => policy.RequireRole(Roles.Optician, Roles.Admin));
    options.AddPolicy(AuthorizationPolicies.AdminOnly,
        policy => policy.RequireRole(Roles.Admin));
});

builder.Services.ConfigureApplicationCookie(options => options.Cookie.Name = "vc_session");

// --- Rate limiting ----------------------------------------------------------
// Section C of the brief. Applied by named policy on the endpoints that need it.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Each policy is partitioned per client. AddFixedWindowLimiter would give the
    // whole policy ONE bucket shared by every visitor, which turns a brute-force
    // defence into a denial-of-service vector: eight bad passwords from anybody
    // would lock every other customer out of signing in for five minutes.
    PerClient("auth", permitLimit: 8);
    PerClient("checkout", permitLimit: 20);
    PerClient("upload", permitLimit: 30);

    void PerClient(string policy, int permitLimit) =>
        options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context, policy),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
            }));
});

// A signed-in visitor is identified by account, so someone behind a shared
// office NAT is not throttled by a colleague. Anonymous requests fall back to
// the connecting address, and a request with neither shares one last bucket —
// which is the safe default, not a hole: it can only ever be more restrictive.
static string ClientKey(HttpContext context, string policy)
{
    var who = context.User.Identity?.IsAuthenticated == true
        ? $"user:{context.User.Identity!.Name}"
        : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    return $"{policy}|{who}";
}

builder.Services.AddResponseCompression(options => options.EnableForHttps = false);

// Shared IIS hosting has no console, so without a file log the application is
// undiagnosable in production. Configured under "FileLog".
builder.Services.Configure<FileLogOptions>(builder.Configuration.GetSection("FileLog"));
builder.Logging.AddVisionCartFileLog(builder.Environment);

builder.Services.AddVisionCartHealthChecks();

// Checked before the host is built, so a misconfigured deployment never opens a
// socket or touches the database.
var configurationWarnings = ProductionConfigurationGuard.Verify(builder.Configuration, builder.Environment);

var app = builder.Build();

// A compromise that was deliberate on the day it was made is not remembered six
// weeks later. Anything the guard allowed under protest says so on every start.
foreach (var warning in configurationWarnings)
{
    app.Logger.LogWarning("Production configuration: {Warning}", warning);
}

// Behind IIS the socket address is the proxy; without this every audit entry
// records the server's own address instead of the client's.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // Customers never see a stack trace.
    app.UseExceptionHandler("/error/500");
    app.UseStatusCodePagesWithReExecute("/error/{0}");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();

app.Use(async (context, next) =>
{
    // Baseline security headers. The try-on needs WebAssembly, so the CSP must
    // permit 'wasm-unsafe-eval' — but nothing wider, and no external origins,
    // which is what keeps the face model local.
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=()";
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'wasm-unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "media-src 'self' blob:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'";

    await next();
});

// The MediaPipe face model has a .task extension, which ASP.NET Core does not
// know. Static file middleware refuses to serve unknown types, so without this
// mapping the model 404s and the virtual try-on silently falls back to manual
// pupil placement on every visit.
var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".task"] = "application/octet-stream";
contentTypes.Mappings[".wasm"] = "application/wasm";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = ctx =>
    {
        // The face model and WASM runtime are 37 MB and immutable; without a
        // long cache every try-on visit re-downloads them.
        var path = ctx.File.Name;
        if (path.EndsWith(".wasm") || path.EndsWith(".task") || path.EndsWith(".png"))
            ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
    },
});

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Areas first: the back office lives under /admin and its controllers carry
// their own attribute routes, but the area token must be registered for
// asp-area link generation to resolve.
// Liveness answers "is the process up"; readiness answers "can it actually
// serve a customer". A load balancer that only ever checks the former will
// happily keep routing traffic at an instance that cannot reach its database.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        // Names and statuses only. The failure text can carry a connection
        // string or a server name, and this endpoint is public.
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
        });
    },
}).AllowAnonymous();

app.MapControllerRoute("areas", "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

// Apply migrations and seed on start, so a shared-hosting deployment needs no
// separate CLI step.
await app.Services.InitialiseDatabaseAsync(app.Environment.WebRootPath);

app.Run();

/// <summary>Exposed so the integration tests can boot the real application.</summary>
public partial class Program;
