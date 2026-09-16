using System.Globalization;

namespace OpenAC.MagTools.Trackers.Combat;

/// <summary>
/// Port of the original's <c>Shared/Util.cs</c> <c>NumberFormatter</c> — the
/// only piece of that file this port needs. Formats <paramref name="number"/>
/// with <paramref name="format"/> unless it would not fit in
/// <paramref name="largestViewableNumber"/>'s digit width, in which case it
/// switches to a k/M/G-suffixed reduced form.
/// </summary>
public static class NumberFormatter
{
    public static string Format(
        long number, string format, int largestViewableNumber, string reducedFormat = "#,##0")
    {
        if (number <= largestViewableNumber)
            return number.ToString(format, CultureInfo.InvariantCulture);

        int spaces = largestViewableNumber.ToString(format, CultureInfo.InvariantCulture).Length;

        string thousands = ((float)number / 1000).ToString(reducedFormat, CultureInfo.InvariantCulture);
        if (thousands.Length <= spaces)
            return thousands + "k";

        string millions = ((float)number / 1_000_000).ToString(reducedFormat, CultureInfo.InvariantCulture);
        if (millions.Length <= spaces)
            return millions + "M";

        return ((float)number / 1_000_000_000).ToString(reducedFormat, CultureInfo.InvariantCulture) + "G";
    }
}
