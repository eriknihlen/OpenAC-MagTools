using System.Globalization;

namespace OpenAC.MagTools.Commands;

/// <summary>
/// Argument shapes for the <c>/mt vendor …</c> family. The commands themselves
/// wait on the vendor automation surface, but their parsing is settled here so
/// the wiring slice only has to add the calls.
/// </summary>
public static class VendorCommandArguments
{
    /// <summary>
    /// <c>addbuy &lt;name&gt; [count]</c>: a trailing whole number is the
    /// count, and its absence means one. A name that is itself a number is
    /// still a name, because the count is only ever the *trailing* token of a
    /// multi-token argument.
    /// </summary>
    public static (string Name, int Count) ParseAddBuy(string? arguments)
    {
        string text = (arguments ?? string.Empty).Trim();
        if (text.Length == 0)
            return (string.Empty, 1);

        int split = text.LastIndexOf(' ');
        if (split <= 0)
            return (text, 1);

        string tail = text[(split + 1)..];
        if (!int.TryParse(
                tail,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int count)
            || count <= 0)
            return (text, 1);

        return (text[..split].Trim(), count);
    }
}
