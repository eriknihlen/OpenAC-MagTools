using System.Text.RegularExpressions;
using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Ui;

namespace OpenAC.MagTools.Macros;

/// <summary>
/// Ports <c>Macros/AutoPercentConfirmation.cs</c>: watches
/// <see cref="IEvents.ConfirmationRequested"/> for a type-5 crafting-percent
/// dialog and answers yes when the reported percent clears the threshold.
/// </summary>
/// <remarks>
/// The original wired TWO independent consumers of the exact same message,
/// but BOTH start with the identical guard
/// <c>if (!Settings.SettingsManager.Tinkering.AutoClickYes.Value) return;</c>
/// (confirmed in both <c>Macros/AutoPercentConfirmation.cs</c> and
/// <c>Views/TinkeringToolsView.cs</c>'s <c>EchoFilter_ServerDispatch</c>) —
/// the tab's own <c>TinkeringMinimumPercent</c> textbox (default "100") is
/// NOT a separate always-on watch, it only replaces the fixed 100% threshold
/// while the setting is already on. With the setting off, NEITHER watcher
/// does anything. This class answers once per confirmation using the LOWER
/// of the two thresholds once the setting is on (answering twice for the
/// same dialog is meaningless anyway, since a second <c>Dialogs.Answer</c>
/// finds nothing outstanding) — collapsing the two watches to one still
/// matches the observable behavior. See docs/deviations.md.
/// </remarks>
public sealed class TinkeringAutoConfirm
{
    /// <summary>Message shape: opcode 0xF7B0, event 0x0274 (confirmation panel), type 5 (crafting percent).</summary>
    public const int ConfirmationEventCode = 0x0274;
    public const int CraftingPercentType = 5;

    private static readonly Regex PercentPattern =
        new("^You determine that you have a (?<percent>.+) percent chance to succeed.$", RegexOptions.Compiled);

    private readonly IPluginHost _host;
    private readonly TinkeringSettings _settings;
    private readonly TinkeringPageViewModel? _tab;
    private Action<PluginConfirmation>? _onConfirmation;
    private bool _running;

    public TinkeringAutoConfirm(IPluginHost host, TinkeringSettings settings, TinkeringPageViewModel? tab = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(settings);
        _host = host;
        _settings = settings;
        _tab = tab;
    }

    /// <summary>
    /// The lowest chance-to-succeed that answers yes, or null when nothing is
    /// armed. Both the fixed 100% watch and the tab's own
    /// <c>TinkeringMinimumPercent</c> watch are gated on the SAME
    /// <c>Tinkering/AutoClickYes</c> setting (H5) — with it off, this is
    /// always null, regardless of the tab's textbox.
    /// </summary>
    public int? MinimumYesPercent
    {
        get
        {
            if (!_settings.AutoClickYes.Value)
                return null;

            int threshold = 100;
            if (_tab is not null && int.TryParse(_tab.MinimumPercentText, out int tabThreshold))
                threshold = Math.Min(threshold, tabThreshold);

            return threshold;
        }
    }

    /// <summary>
    /// Parses <paramref name="text"/> against the retail confirmation
    /// sentence and returns the percent, or null when it doesn't match.
    /// </summary>
    public static int? TryParsePercent(string text)
    {
        Match match = PercentPattern.Match(text);
        return match.Success && int.TryParse(match.Groups["percent"].Value, out int percent) ? percent : null;
    }

    public void Start()
    {
        if (_running)
            return;
        _running = true;

        _onConfirmation = OnConfirmationRequested;
        _host.Events.ConfirmationRequested += _onConfirmation;
    }

    public void Stop()
    {
        if (!_running)
            return;
        _running = false;

        if (_onConfirmation is not null)
            _host.Events.ConfirmationRequested -= _onConfirmation;
        _onConfirmation = null;
    }

    private void OnConfirmationRequested(PluginConfirmation confirmation)
    {
        if (confirmation.Type != CraftingPercentType)
            return;

        int? threshold = MinimumYesPercent;
        if (threshold is null)
            return;

        int? percent = TryParsePercent(confirmation.Text);
        if (percent is { } value && value >= threshold)
            _host.Automation.Dialogs.Answer(confirmation.ContextId, true);
    }
}
