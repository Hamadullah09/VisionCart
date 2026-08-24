using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VisionCart.Domain.Constants;
using VisionCart.Domain.Entities;

namespace VisionCart.IntegrationTests.Http;

/// <summary>
/// Boots the real application in-process and drives it over HTTP.
///
/// Everything else in this suite calls services directly, which leaves an entire
/// layer untested: routing, model binding, antiforgery, the authorisation
/// policies and the response headers. That gap was not theoretical — the media
/// uploader shipped with an empty antiforgery token and rejected every upload
/// with a 400 while its service-level tests stayed green.
///
/// Signing in is expensive here, and not for the usual reason: the sign-in
/// endpoint is rate limited, so a suite that logged in per test would throttle
/// itself. One client per role is created once and reused.
/// </summary>
public sealed class VisionCartApp : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Header the test-only middleware turns into a remote IP address.</summary>
    public const string ClientIpHeader = "X-Test-Client-Ip";

    private static readonly WebApplicationFactoryClientOptions NoRedirect = new()
    {
        // Redirects are the assertion in most authorisation tests: an anonymous
        // request to the back office must *become* a trip to /login, and that is
        // invisible once the handler has followed it.
        AllowAutoRedirect = false,
    };

    public HttpClient Anonymous { get; private set; } = null!;
    public HttpClient Customer { get; private set; } = null!;
    public HttpClient Staff { get; private set; } = null!;
    public HttpClient Admin { get; private set; } = null!;

    public string CustomerEmail { get; } = $"http-customer-{Guid.NewGuid():N}@example.com";
    private const string CustomerPassword = "TestOnly!Cust9";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The seeded staff passwords live in the Development configuration, and
        // the seeder never rewrites an existing account's password — so the tests
        // must read the same file the application does rather than hardcode a
        // literal that would silently drift out of date.
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStartupFilter, ClientIpOverrideStartupFilter>();
        });
    }

    public async Task InitializeAsync()
    {
        Anonymous = CreateClient(NoRedirect);

        var config = Services.GetRequiredService<IConfiguration>();
        var adminEmail = config["Seed:AdminEmail"]
            ?? throw new InvalidOperationException("Seed:AdminEmail is not configured.");
        var adminPassword = config["Seed:AdminPassword"]
            ?? throw new InvalidOperationException("Seed:AdminPassword is not configured.");
        var opticianEmail = config["Seed:OpticianEmail"]
            ?? throw new InvalidOperationException("Seed:OpticianEmail is not configured.");
        var opticianPassword = config["Seed:OpticianPassword"]
            ?? throw new InvalidOperationException("Seed:OpticianPassword is not configured.");

        await CreateCustomerAsync();

        Admin = await SignedInClientAsync(adminEmail, adminPassword);
        Staff = await SignedInClientAsync(opticianEmail, opticianPassword);
        Customer = await SignedInClientAsync(CustomerEmail, CustomerPassword);
    }

    /// <summary>
    /// Created through <see cref="UserManager{T}"/> rather than the registration
    /// form: registration is rate limited alongside sign-in, and this account is a
    /// fixture, not the thing under test.
    /// </summary>
    private async Task CreateCustomerAsync()
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = CustomerEmail,
            Email = CustomerEmail,
            EmailConfirmed = true,
            Name = "HTTP Test Customer",
            Role = Roles.Customer,
            IsActive = true,
        };

        var created = await users.CreateAsync(user, CustomerPassword);
        if (!created.Succeeded)
            throw new InvalidOperationException(
                "Could not create the test customer: " +
                string.Join("; ", created.Errors.Select(e => e.Description)));

        await users.AddToRoleAsync(user, Roles.Customer);
    }

    private async Task<HttpClient> SignedInClientAsync(string email, string password)
    {
        var client = CreateClient(NoRedirect);
        var response = await SignInAsync(client, email, password);

        if (response.StatusCode != HttpStatusCode.Redirect)
            throw new InvalidOperationException(
                $"Sign-in for {email} returned {(int)response.StatusCode}, expected a redirect. " +
                "The seeded password may not match the configured one.");

        return client;
    }

    /// <summary>Posts the sign-in form, antiforgery token and all.</summary>
    public async Task<HttpResponseMessage> SignInAsync(
        HttpClient client, string email, string password, string? clientIp = null)
    {
        var token = await AntiforgeryTokenAsync(client, "/login");

        return await PostFormAsync(client, "/login", new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
        }, clientIp);
    }

    /// <summary>
    /// Fetches a page and lifts its antiforgery token out of the rendered HTML.
    ///
    /// Scraping the real markup is deliberate. A token read from the framework
    /// instead would still have passed while the view was emitting a broken one.
    /// </summary>
    public async Task<string> AntiforgeryTokenAsync(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        var match = Regex.Match(
            html, """<input[^>]*name="__RequestVerificationToken"[^>]*value="([^"]+)""");

        if (!match.Success)
            throw new InvalidOperationException(
                $"No antiforgery token was rendered on {url}. Any form on that page " +
                "will be rejected with a 400.");

        return match.Groups[1].Value;
    }

    public static Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string url, IDictionary<string, string> fields, string? clientIp = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(fields),
        };

        if (clientIp is not null) request.Headers.Add(ClientIpHeader, clientIp);
        return client.SendAsync(request);
    }

    public new async Task DisposeAsync()
    {
        // Leave no test account behind in a database the developer also browses.
        using (var scope = Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(CustomerEmail);
            if (user is not null) await users.DeleteAsync(user);
        }

        Anonymous?.Dispose();
        Customer?.Dispose();
        Staff?.Dispose();
        Admin?.Dispose();

        await base.DisposeAsync();
    }
}

/// <summary>
/// Lets a test present itself as coming from a chosen IP address.
///
/// TestServer reports no remote address, so every in-process request would
/// otherwise land in the same rate-limiting partition and a per-client limit
/// would be indistinguishable from a global one. This is test-only plumbing
/// registered through <c>ConfigureTestServices</c>; the application pipeline is
/// untouched.
/// </summary>
internal sealed class ClientIpOverrideStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(VisionCartApp.ClientIpHeader, out var raw) &&
                IPAddress.TryParse(raw.ToString(), out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            await nextMiddleware();
        });

        next(app);
    };
}

[CollectionDefinition("http")]
public sealed class HttpCollection : ICollectionFixture<VisionCartApp>;
