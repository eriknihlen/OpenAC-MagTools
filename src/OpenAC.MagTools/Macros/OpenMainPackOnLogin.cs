using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Macros;

/// <summary>
/// Port of the original's §1.14 <c>OpenMainPackOnLogin</c>: on
/// <c>LoginComplete</c>, if <c>Misc/OpenMainPackOnLogin</c> is set (default
/// true), open the character's main pack -- the original's
/// <c>Actions.UseItem(myId, 0)</c>, i.e. "use" the player's own object.
/// </summary>
/// <remarks>
/// MECHANISM DEVIATION (resolved 2026-09-16 via OpenAC slice A6): the
/// original opened the pack as a side effect of "using" the player's own
/// object; that call is refused by this host (<c>IsPlayerOwned</c> excludes
/// the player object itself -- see <c>docs/deviations.md</c>'s H1 row for
/// the earlier PENDING state). A6 added a direct client-window surface, so
/// this now calls <see cref="IUiRegistry.ShowClientWindow"/> with
/// <see cref="PluginClientWindow.Inventory"/> instead -- same observable
/// result (the main pack opens), different route. Never swallows a refusal:
/// logs a warning when the host reports the window did not end up visible.
/// No-op on a no-window host (<see cref="IPluginHost.HasUi"/> false), same
/// as every other UI call in this plugin.
/// </remarks>
public sealed class OpenMainPackOnLogin
{
    private readonly IPluginHost _host;
    private readonly Setting<bool> _enabled;

    public OpenMainPackOnLogin(IPluginHost host, Setting<bool> enabled)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(enabled);
        _host = host;
        _enabled = enabled;
    }

    /// <summary>Called once per <c>LoginComplete</c> edge.</summary>
    public void Run()
    {
        if (!_enabled.Value)
            return;
        if (!_host.HasUi)
            return;

        bool shown = _host.Ui.ShowClientWindow(PluginClientWindow.Inventory);
        if (!shown)
        {
            _host.Log.Warn(
                "OpenMainPackOnLogin: Ui.ShowClientWindow(Inventory) did not "
                + "end up visible.");
        }
    }
}
