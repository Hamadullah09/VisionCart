using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using VisionCart.Web.Services;

namespace VisionCart.UnitTests;

/// <summary>
/// The guard that refuses to start Production with development configuration.
///
/// It is only worth having if it actually fires, so each case here is a real
/// deployment mistake: the connection string nobody replaced, the demo password
/// that reached a live database, the mail driver that quietly swallowed every
/// order confirmation.
/// </summary>
public class ProductionGuardTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "VisionCart.Web";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    /// <summary>A configuration that should pass, so each test can break one thing.</summary>
    private static Dictionary<string, string?> Good() => new()
    {
        ["ConnectionStrings:DefaultConnection"] = "Server=sql.example.net;Database=VisionCart;User Id=vc;Password=x",
        ["Seed:Enabled"] = "false",
        ["Email:Driver"] = "smtp",
        ["Email:Host"] = "smtp.example.net",
        ["Payments:Providers"] = "cod,bank_transfer",
        ["AllowedHosts"] = "shop.example.com",
    };

    private static IReadOnlyList<string> Verify(
        Dictionary<string, string?> settings, string environment = "Production") =>
        ProductionConfigurationGuard.Verify(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), new Env(environment));

    private static string Refusal(Dictionary<string, string?> settings) =>
        Assert.Throws<InvalidOperationException>(() => Verify(settings)).Message;

    [Fact]
    public void A_complete_configuration_starts()
    {
        Verify(Good());
    }

    [Fact]
    public void Development_is_never_blocked()
    {
        // The guard must not make local work harder; LocalDB and the log mail
        // driver are correct there.
        Verify(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = @"Server=(localdb)\VisionCartDev;Database=VisionCart",
            ["Email:Driver"] = "log",
            ["AllowedHosts"] = "*",
        }, environment: "Development");
    }

    [Fact]
    public void A_localdb_connection_string_is_refused()
    {
        var settings = Good();
        settings["ConnectionStrings:DefaultConnection"] = @"Server=(localdb)\VisionCartDev;Database=VisionCart";

        Assert.Contains("LocalDB", Refusal(settings));
    }

    [Fact]
    public void An_empty_connection_string_is_refused()
    {
        var settings = Good();
        settings["ConnectionStrings:DefaultConnection"] = "";

        Assert.Contains("ConnectionStrings__DefaultConnection", Refusal(settings));
    }

    [Fact]
    public void A_published_demo_password_never_reaches_a_live_database()
    {
        var settings = Good();
        settings["Seed:Enabled"] = "true";
        settings["Seed:AdminPassword"] = "DevOnly!Change9";

        Assert.Contains("demo password", Refusal(settings));
    }

    [Fact]
    public void The_log_mail_driver_is_refused_because_nobody_would_receive_anything()
    {
        var settings = Good();
        settings["Email:Driver"] = "log";

        Assert.Contains("order confirmation", Refusal(settings));
    }

    [Fact]
    public void The_log_driver_escape_hatch_is_off_unless_set()
    {
        // A false value is not an opt-in, and neither is an absent one.
        var settings = Good();
        settings["Email:Driver"] = "log";
        settings["Email:AllowLogDriverInProduction"] = "false";

        Assert.Contains("order confirmation", Refusal(settings));
    }

    [Fact]
    public void The_log_driver_is_allowed_when_somebody_asks_for_it_by_name()
    {
        // Bringing a site up before its mailbox exists is a real situation. It
        // has to be said out loud, though, not reached by leaving a default.
        var settings = Good();
        settings["Email:Driver"] = "log";
        settings["Email:AllowLogDriverInProduction"] = "true";

        var warnings = Verify(settings);

        var warning = Assert.Single(warnings);
        Assert.Contains("Nobody is receiving mail", warning);
    }

    [Fact]
    public void The_log_driver_warns_that_queued_mail_is_never_delivered_later()
    {
        // The trap: the log sender marks each message sent, so configuring SMTP
        // afterwards does not go back and deliver anything.
        var settings = Good();
        settings["Email:Driver"] = "log";
        settings["Email:AllowLogDriverInProduction"] = "true";

        Assert.Contains("will NOT deliver", Assert.Single(Verify(settings)));
    }

    [Fact]
    public void A_configuration_with_nothing_wrong_warns_about_nothing()
    {
        Assert.Empty(Verify(Good()));
    }

    [Fact]
    public void Smtp_without_a_host_is_refused()
    {
        var settings = Good();
        settings["Email:Host"] = "";

        Assert.Contains("Email:Host is empty", Refusal(settings));
    }

    [Fact]
    public void Offering_stripe_without_a_key_is_refused()
    {
        // Otherwise checkout shows a card option that cannot take a payment.
        var settings = Good();
        settings["Payments:Providers"] = "cod,stripe";

        Assert.Contains("StripeSecretKey", Refusal(settings));
    }

    [Fact]
    public void A_wildcard_host_header_is_refused()
    {
        var settings = Good();
        settings["AllowedHosts"] = "*";

        Assert.Contains("AllowedHosts", Refusal(settings));
    }

    [Fact]
    public void Every_problem_is_reported_at_once_rather_than_one_per_deploy()
    {
        var settings = Good();
        settings["ConnectionStrings:DefaultConnection"] = "";
        settings["Email:Driver"] = "log";
        settings["AllowedHosts"] = "*";

        var message = Refusal(settings);

        // Fixing one, redeploying, and discovering the next is three wasted
        // deployment cycles on a host where each one costs minutes.
        Assert.Equal(3, message.Split("  - ").Length - 1);
    }
}
