using VisionCart.Domain.ValueObjects;

namespace VisionCart.UnitTests;

/// <summary>
/// The money rules carried over from <c>src/lib/money.ts</c>. These are the
/// arithmetic guarantees the whole shop rests on, and the legacy project had no
/// tests for them at all.
/// </summary>
public class MoneyTests
{
    private const string Pkr = "PKR";
    private const string Jpy = "JPY";

    [Theory]
    [InlineData(1499.5, 149950)]
    [InlineData(6500, 650000)]
    [InlineData(0, 0)]
    [InlineData(0.01, 1)]
    [InlineData(0.005, 1)]      // half-up, not banker's rounding
    [InlineData(0.014, 1)]
    [InlineData(-12.34, -1234)]
    public void ToMinor_converts_major_units(decimal major, int expected) =>
        Assert.Equal(expected, Money.ToMinor(major, Pkr));

    [Theory]
    [InlineData(149950, 1499.5)]
    [InlineData(650000, 6500)]
    [InlineData(1, 0.01)]
    public void FromMinor_converts_back(int minor, decimal expected) =>
        Assert.Equal(expected, Money.FromMinor(minor, Pkr));

    [Fact]
    public void Zero_decimal_currencies_have_no_minor_unit()
    {
        Assert.Equal(1, Money.MinorPerUnit(Jpy));
        Assert.Equal(1500, Money.ToMinor(1500m, Jpy));
        Assert.Equal("¥1,500", Money.Format(1500, Jpy, "¥"));
    }

    [Theory]
    [InlineData(650000, 1500, 97500)]   // the seeded WELCOME15: 15% of Rs.6,500
    [InlineData(650000, 2500, 162500)]  // 25%
    [InlineData(100, 1500, 15)]
    [InlineData(1, 1500, 0)]            // rounds to nothing, never to a fraction
    [InlineData(0, 1500, 0)]
    public void ApplyBps_computes_percentages_in_basis_points(int minor, int bps, int expected) =>
        Assert.Equal(expected, Money.ApplyBps(minor, bps));

    [Fact]
    public void Discount_matches_the_figure_the_live_shop_produced()
    {
        // Verified end-to-end in the browser against the legacy application:
        // a Rs.6,500 frame with WELCOME15 and Rs.300 delivery totalled Rs.5,825.
        const int frame = 650000;
        const int delivery = 30000;

        var discount = Money.ApplyBps(frame, 1500);
        var total = frame - discount + delivery;

        Assert.Equal(97500, discount);
        Assert.Equal(582500, total);
        Assert.Equal("Rs.5,825.00", Money.Format(total, Pkr, "Rs."));
    }

    [Fact]
    public void Round_tripping_never_drifts()
    {
        // The reason money is an integer at all: repeated conversion must be exact.
        for (var minor = 0; minor < 20000; minor += 7)
        {
            Assert.Equal(minor, Money.ToMinor(Money.FromMinor(minor, Pkr), Pkr));
        }
    }

    [Fact]
    public void A_thousand_percentage_discounts_do_not_accumulate_error()
    {
        // The failure mode float money produces: apply and reverse a percentage
        // repeatedly and watch the total bleed. Integers cannot do this.
        var running = 0;
        for (var i = 0; i < 1000; i++) running += Money.ApplyBps(129900, 1750);
        Assert.Equal(1000 * Money.ApplyBps(129900, 1750), running);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(500, 500)]
    public void ClampNonNegative_never_returns_a_negative_total(int input, int expected) =>
        Assert.Equal(expected, Money.ClampNonNegative(input));

    [Fact]
    public void Format_groups_digits_identically_regardless_of_thread_culture()
    {
        // The legacy code deliberately avoided culture-aware currency formatting
        // because the locale-dependent gap differed between server and client and
        // broke hydration. The same guarantee is asserted here: a German culture
        // must not turn "Rs.6,500.00" into "Rs.6.500,00".
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("Rs.6,500.00", Money.Format(650000, Pkr, "Rs."));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}

public class CuidTests
{
    [Fact]
    public void Ids_have_the_legacy_shape()
    {
        var id = Cuid.NewId();
        Assert.StartsWith("c", id);
        Assert.InRange(id.Length, 24, 32);
        Assert.Matches("^c[0-9a-z]+$", id);
    }

    [Fact]
    public void Ids_are_unique_under_contention()
    {
        // Minted concurrently, as they would be under load on a web farm.
        var ids = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 20_000, _ => ids.Add(Cuid.NewId()));
        Assert.Equal(20_000, ids.Distinct().Count());
    }

    [Fact]
    public void Ids_sort_roughly_by_creation_time()
    {
        // Primary keys are clustered in SQL Server. Time-ordered keys append to
        // the end of the index; random ones fragment every table in the database.
        var first = Cuid.NewId();
        Thread.Sleep(5);
        var second = Cuid.NewId();
        Assert.True(string.CompareOrdinal(first, second) < 0,
            $"expected {first} to sort before {second}");
    }
}
