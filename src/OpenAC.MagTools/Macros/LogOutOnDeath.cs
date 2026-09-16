using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Macros;

/// <summary>
/// Port of the original's §1.14 <c>LogOutOnDeath</c>: when
/// <c>Misc/LogOutOnDeath</c> is set (default false) and the local player
/// dies, log out through the client's own graceful route -- the original's
/// <c>CharacterFilter.Death → Actions.Logout()</c>.
/// </summary>
/// <remarks>
/// Subscribed for the plugin's whole enabled lifetime (like
/// <see cref="SessionContext"/> itself), not per-session, since
/// <see cref="IEvents.LocalPlayerDied"/> can only fire while in-world anyway.
/// </remarks>
public sealed class LogOutOnDeath
{
    private readonly IPluginHost _host;
    private readonly Setting<bool> _enabled;
    private readonly Action<string> _onDied;
    private bool _subscribed;

    public LogOutOnDeath(IPluginHost host, Setting<bool> enabled)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(enabled);
        _host = host;
        _enabled = enabled;
        _onDied = OnLocalPlayerDied;
    }

    /// <summary>How many times a death has triggered a logout attempt -- exposed for tests.</summary>
    internal int LogoutAttempts { get; private set; }

    public void Start()
    {
        if (_subscribed)
            return;
        _host.Events.LocalPlayerDied += _onDied;
        _subscribed = true;
    }

    public void Stop()
    {
        if (!_subscribed)
            return;
        _host.Events.LocalPlayerDied -= _onDied;
        _subscribed = false;
    }

    private void OnLocalPlayerDied(string deathMessage)
    {
        if (!_enabled.Value)
            return;

        LogoutAttempts++;
        _host.Automation.Login.Logout();
    }
}
