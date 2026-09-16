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
/// OpenAC's <see cref="IItemAutomation.Use(uint)"/> called with the local
/// player's own object id is the client's equivalent of that same
/// self-use-opens-main-pack retail behavior.
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

        _host.Automation.Items.Use(_host.Automation.Character.ObjectId);
    }
}
