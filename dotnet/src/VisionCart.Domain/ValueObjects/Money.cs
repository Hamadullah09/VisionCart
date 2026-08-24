using System.Globalization;

namespace VisionCart.Domain.ValueObjects;

/// <summary>
/// Port of the original <c>src/lib/money.ts</c>.
///
/// All money in this app is an integer count of minor units (paisa, cents).
/// Decimals are only ever produced at the last moment, for display. This is a
/// hard invariant carried over from the original system: no floating-point
/// arithmetic is ever performed on a price.
/// </summary>
public static class Money
{
    /// <summary>Currencies whose smallest unit is the unit itself (no decimal places).</summary>
    private static readonly HashSet<string> ZeroDecimal =
        new(StringComparer.OrdinalIgnoreCase) { "JPY", "KRW", "VND", "CLP", "ISK", "XAF", "XOF" };

    public static int MinorPerUnit(string currency) => ZeroDecimal.Contains(currency) ? 1 : 100;

    /// <summary>1499.5 -&gt; 149950</summary>
    public static int ToMinor(decimal amount, string currency) =>
        (int)Math.Round(amount * MinorPerUnit(currency), MidpointRounding.AwayFromZero);

    /// <summary>149950 -&gt; 1499.5</summary>
    public static decimal FromMinor(int minor, string currency) =>
        (decimal)minor / MinorPerUnit(currency);

    /// <summary>
    /// Display helper. Deliberately not a culture-aware currency format — the
    /// original avoided <c>Intl.NumberFormat</c> with <c>style: "currency"</c>
    /// because the locale-dependent gap shifted between server and client and
    /// tripped React hydration. A fixed symbol plus grouped digits renders
    /// identically everywhere, so the same choice is kept here.
    /// </summary>
    public static string Format(int minor, string currency, string symbol)
    {
        var decimals = MinorPerUnit(currency) == 1 ? 0 : 2;
        var value = FromMinor(minor, currency);
        var body = value.ToString("N" + decimals, CultureInfo.InvariantCulture);
        return symbol + body;
    }

    /// <summary>Round-half-up percentage of a minor amount. bps: 1500 = 15%.</summary>
    public static int ApplyBps(int minor, int bps) =>
        (int)Math.Round((decimal)minor * bps / 10000m, MidpointRounding.AwayFromZero);

    public static int ClampNonNegative(int minor) => minor < 0 ? 0 : minor;
}
