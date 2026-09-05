using VisionCart.Domain.Constants;

namespace VisionCart.Web.Services;

/// <summary>
/// Refuses to start in Production with development configuration.
///
/// The failure this prevents is quiet and expensive: an application that boots
/// perfectly against a connection string nobody replaced, seeds a published demo
/// password into a live database, and writes every order confirmation to a log
/// file instead of sending it. Each of those looks like a working deployment
/// from the outside.
///
/// Failing at startup is the point. A shared host restarts the process on the
/// next request, so a misconfigured deployment stays down and visible rather
/// than serving customers badly.
/// </summary>
public static class ProductionConfigurationGuard
{
    /// <summary>
    /// Passwords that must never reach production, whatever the documentation
    /// said. The legacy application published its demo credentials in its README.
    /// </summary>
    private static readonly string[] BannedPasswords =
    [
        "DevOnly!Change9", "DevOnly!Optic9",
        "password", "Password1", "admin", "changeme", "visioncart",
    ];

    /// <summary>
    /// Throws if the configuration must not start. Returns anything that is
    /// allowed but should not be quiet — a deliberate compromise still has to be
    /// visible in the log every time the process starts.
    /// </summary>
    public static IReadOnlyList<string> Verify(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsProduction()) return [];

        var problems = new List<string>();
        var warnings = new List<string>();

        // --- database ---
        var connection = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connection))
            problems.Add(
                "ConnectionStrings:DefaultConnection is empty. Set ConnectionStrings__DefaultConnection.");
        else if (connection.Contains("(localdb)", StringComparison.OrdinalIgnoreCase))
            problems.Add(
                "ConnectionStrings:DefaultConnection still points at LocalDB, which does not exist on a server.");

        // --- seeding ---
        if (configuration.GetValue("Seed:Enabled", false))
        {
            var seedPassword = configuration["Seed:AdminPassword"];

            if (!string.IsNullOrWhiteSpace(seedPassword) && IsBanned(seedPassword))
                problems.Add(
                    "Seed:AdminPassword is a published demo password. Choose a real one, " +
                    "or disable seeding once the first administrator exists.");
        }

        // --- email ---
        var driver = configuration["Email:Driver"];

        if (string.Equals(driver, "log", StringComparison.OrdinalIgnoreCase))
        {
            // Allowed only when someone has said so by name. Bringing a site up
            // before its mailbox exists is a real situation; discovering months
            // later that no customer ever got a confirmation is not.
            if (configuration.GetValue("Email:AllowLogDriverInProduction", false))
                warnings.Add(
                    "Email:Driver is 'log'. Nobody is receiving mail: every message is written " +
                    "to the log and marked sent, so switching to smtp later will NOT deliver " +
                    "anything queued in the meantime. Email:AllowLogDriverInProduction is set, " +
                    "so this is deliberate — clear it once SMTP is configured.");
            else
                problems.Add(
                    "Email:Driver is 'log', so no customer would receive an order confirmation. " +
                    "Set Email__Driver=smtp and configure a host, or set " +
                    "Email__AllowLogDriverInProduction=true to accept that deliberately.");
        }
        else if (string.Equals(driver, "smtp", StringComparison.OrdinalIgnoreCase)
                 && string.IsNullOrWhiteSpace(configuration["Email:Host"]))
            problems.Add("Email:Driver is 'smtp' but Email:Host is empty.");

        // --- payments ---
        var providers = configuration["Payments:Providers"] ?? string.Empty;

        if (providers.Contains(PaymentProviders.Stripe, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(configuration["Payments:StripeSecretKey"]))
            problems.Add(
                "Stripe is listed in Payments:Providers but Payments:StripeSecretKey is empty. " +
                "Checkout would offer a card option that cannot take a payment.");

        // --- host header ---
        // "*" lets the site answer to any Host header, which is what makes cache
        // poisoning and password-reset link forgery possible.
        if (configuration["AllowedHosts"] is null or "*")
            problems.Add("AllowedHosts is '*'. Set it to the site's own domain(s).");

        if (problems.Count == 0) return warnings;

        throw new InvalidOperationException(
            "VisionCart refused to start because its production configuration is incomplete:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, problems.Select(p => "  - " + p))
            + Environment.NewLine
            + "See docs/07-deployment.md. Nothing has been started and no data has been touched.");
    }

    private static bool IsBanned(string password) =>
        BannedPasswords.Any(banned => string.Equals(banned, password, StringComparison.OrdinalIgnoreCase));
}
