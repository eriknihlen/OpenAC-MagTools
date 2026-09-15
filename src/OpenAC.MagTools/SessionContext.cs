using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools;

/// <summary>
/// The in-world edge detector every login/logoff behaviour hangs off.
/// </summary>
/// <remarks>
/// A reconnect does not recycle plugins: registrations survive a generation
/// reset, so the plugin has to notice for itself that the session went away and
/// came back. The contract has no login event yet, so the edge is derived by
/// polling <c>Automation.IsAvailable &amp;&amp; Character.IsInWorld</c> each
/// tick. When the lifecycle events land this class swaps its poll for them and
/// nothing downstream changes.
/// </remarks>
public sealed class SessionContext
{
    private readonly IPluginHost _host;
    private readonly ChatOutput _chat;

    public SessionContext(IPluginHost host, ChatOutput chat)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(chat);
        _host = host;
        _chat = chat;
    }

    /// <summary>True between the login edge and the matching logoff edge.</summary>
    public bool IsInWorld { get; private set; }

    public string CharacterName { get; private set; } = string.Empty;

    public string WorldName { get; private set; } = string.Empty;

    public string AccountName { get; private set; } = string.Empty;

    /// <summary>Raised once each time the session becomes in-world.</summary>
    public event Action? LoginComplete;

    /// <summary>Raised once each time an in-world session goes away.</summary>
    public event Action? Logoff;

    /// <summary>Samples the host and raises an edge if one occurred.</summary>
    public void Poll()
    {
        IAutomationSurface automation = _host.Automation;
        bool inWorld = automation.IsAvailable && automation.Character.IsInWorld;
        if (inWorld == IsInWorld)
            return;

        if (inWorld)
        {
            ICharacterInfo character = automation.Character;
            CharacterName = character.Name;
            WorldName = character.WorldName;
            AccountName = character.AccountName;
            IsInWorld = true;

            // TODO(E-LIFECYCLE): the original printed
            // "Plugin now online. Server population: <N>". Restore the
            // population clause once ICharacterInfo.ServerPopulation exists.
            _chat.Write("Plugin now online.");
            LoginComplete?.Invoke();
            return;
        }

        IsInWorld = false;
        Logoff?.Invoke();
        CharacterName = string.Empty;
        WorldName = string.Empty;
        AccountName = string.Empty;
    }

    /// <summary>
    /// Clears the edge state without raising <see cref="Logoff"/>. The plugin
    /// calls this from <c>Disable()</c> so a Disable/Enable cycle (a plugin
    /// reload with no underlying reconnect) polls from a clean slate and
    /// re-fires <see cref="LoginComplete"/> instead of staying silent because
    /// <see cref="IsInWorld"/> was already true.
    /// </summary>
    public void Reset()
    {
        IsInWorld = false;
        CharacterName = string.Empty;
        WorldName = string.Empty;
        AccountName = string.Empty;
    }
}
