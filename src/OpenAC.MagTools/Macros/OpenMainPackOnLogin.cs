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
/// <para>
/// PENDING HOST SURFACE (H1, 2026-09-16): <see cref="IItemAutomation.Use(uint)"/>
/// called with the local player's own object id is REJECTED by the current
/// host -- <c>IsPlayerOwned</c> excludes the player object itself, and the
/// inventory window otherwise opens only through a local input action,
/// unreachable from a plugin today. An OpenAC slice A6 is adding
/// <c>IUiRegistry.ToggleClientWindow</c>/<c>ShowClientWindow</c>/
/// <c>HideClientWindow</c>/<c>IsClientWindowVisible(PluginClientWindow)</c>;
/// once that lands, <see cref="Run"/> should call
/// <c>Ui.ShowClientWindow(PluginClientWindow.Inventory)</c> instead. Until
/// then this method still issues the original's <c>Use(self)</c> call (in
/// case a future host build accepts it) but NEVER swallows a non-<c>Started</c>
/// result -- it logs a warning so the gap stays visible instead of silently
/// no-opping. The OpenAC-native equivalent today is the client's own F12
/// keybind. See <c>docs/deviations.md</c> and <c>docs/live-results.md</c>
/// (marked Pending, not Shipped, until A6 lands and this is re-gated).
/// </para>
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

        PluginItemCommandResult result =
            _host.Automation.Items.Use(_host.Automation.Character.ObjectId);
        if (!result.Accepted)
        {
            _host.Log.Warn(
                "OpenMainPackOnLogin: Items.Use(self) did not start (status="
                + result.Status
                + (result.Notice is null ? string.Empty : ", " + result.Notice)
                + "). Pending OpenAC slice A6 (Ui.ShowClientWindow) -- see "
                + "docs/live-results.md.");
        }
    }
}
