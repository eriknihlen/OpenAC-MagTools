using OpenAC.MagTools.Trackers.Combat;

namespace OpenAC.MagTools.Tests.Trackers.Combat;

/// <summary>
/// Every expected string here was computed by hand against the original's
/// exact algorithm (see NumberFormatter.cs's port note): compare the plain
/// <c>"#,##0"</c>-formatted <paramref name="largestViewableNumber"/> width in
/// characters ("spaces") against each reduced form's width, first k, then M,
/// then G, first one that fits wins.
/// </summary>
public sealed class NumberFormatterTests
{
    [Fact]
    public void ValueAtOrBelowTheCapIsPlainFormatted()
        => Assert.Equal("99,999", NumberFormatter.Format(99_999, "#,##0", 99_999));

    [Fact]
    public void ValueJustAboveTheCapSwitchesToThousands()
    {
        // Cap 99,999 formats to "99,999" (6 chars). 100,000/1000 = "100" (3
        // chars) fits within 6.
        Assert.Equal("100k", NumberFormatter.Format(100_000, "#,##0", 99_999));
    }

    [Fact]
    public void ThousandsFormThatWouldNotFitFallsThroughToMillions()
    {
        // Cap 999 formats to "999" (3 chars, "spaces" = 3).
        // 12,345,678 / 1000 = 12,345.678 -> rounds to "12,346" (6 chars),
        // too wide for 3 -> falls to millions: 12,345,678 / 1e6 = 12.345678
        // -> rounds to "12" (2 chars), fits within 3.
        Assert.Equal("12M", NumberFormatter.Format(12_345_678, "#,##0", 999));
    }

    [Fact]
    public void MillionsFormThatWouldNotFitFallsThroughToBillions()
    {
        // Cap 9 formats to "9" (1 char, "spaces" = 1). Neither the thousands
        // form ("5,000,000", 9 chars) nor the millions form ("5,000", 5
        // chars) fit within 1 -> falls all the way to billions: "5" (1
        // char) fits.
        Assert.Equal("5G", NumberFormatter.Format(5_000_000_000, "#,##0", 9));
    }

    [Fact]
    public void ZeroIsPlainFormatted()
        => Assert.Equal("0", NumberFormatter.Format(0, "#,##0", 99_999));
}
