using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Ui;

/// <summary>
/// The binding object for <c>magtools-hud.xml</c>: the 14 status rows the
/// original wrote into the Virindi HUDs status bar, in the same order.
/// </summary>
/// <remarks>
/// The original was never a drawn overlay either — it handed rows to a status
/// window someone else positioned. Here it is an ordinary small plugin window.
/// TODO(P4/P5): the trackers that produce these numbers fill
/// <see cref="Values"/>; an empty value means "nothing to show", as it did in
/// the original.
/// </remarks>
public sealed class HudViewModel
{
    /// <summary>The row captions, in the original's order.</summary>
    public static readonly IReadOnlyList<string> RowNames =
    [
        "Mana",
        "Comps Time 1h",
        "Net Profit 5m",
        "Net Profit 1h",
        "DPS Out 1m",
        "DPS Out 5m",
        "DPS Out 1h",
        "DPS In 1m",
        "DPS In 5m",
        "DPS In 1h",
        "Players",
        "Monsters",
        "Pack Slots",
        "ID Queue",
    ];

    private readonly string[] _values = new string[RowNames.Count];
    private readonly IPluginHost _host;

    public HudViewModel(IPluginHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        Array.Fill(_values, string.Empty);
        Select = index => SelectedRow = index;
    }

    /// <summary>
    /// The root <c>visible</c> binding is an availability gate, not the
    /// current shown/hidden state. Unlike the main window, the HUD's rows are
    /// all live-session numbers, so it follows the same gate MossTank uses:
    /// there is nothing to show before an automation session exists.
    /// </summary>
    public bool WindowAvailable => _host.Automation.IsAvailable;

    public IReadOnlyList<string> Names => RowNames;

    public IReadOnlyList<string> Values => _values;

    public int SelectedRow { get; private set; } = -1;

    public Action<int> Select { get; }

    /// <summary>Sets one row's value. An empty string blanks the row.</summary>
    public void SetValue(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int index = -1;
        for (int candidate = 0; candidate < RowNames.Count; candidate++)
        {
            if (!string.Equals(RowNames[candidate], name, StringComparison.Ordinal))
                continue;
            index = candidate;
            break;
        }

        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(name), name);

        _values[index] = value;
    }
}
