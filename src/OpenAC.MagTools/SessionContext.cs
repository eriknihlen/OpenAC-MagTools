using System.Globalization;
using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools;

/// <summary>
/// The in-world edge detector every login/logoff behaviour hangs off.
/// </summary>
/// <remarks>
/// A reconnect does not recycle plugins: registrations survive a generation
/// reset, so the plugin has to notice for itself that the session went away and
/// came back. Since slice A1 the host contract has real
/// <see cref="IEvents.LoginComplete"/>/<see cref="IEvents.Logoff"/> events, so this
/// class subscribes to those directly instead of polling <c>Automation.IsAvailable
/// &amp;&amp; Character.IsInWorld</c> every tick.
/// </remarks>
public sealed class SessionContext
{
    private readonly IPluginHost _host;
    private readonly ChatOutput _chat;
    private readonly Action _onLoginComplete;
    private readonly Action _onLogoff;
    private readonly Action<double> _onNameResolutionTick;
    private bool _subscribed;
    private bool _nameResolutionPending;

    public SessionContext(IPluginHost host, ChatOutput chat)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(chat);
        _host = host;
        _chat = chat;
        _onLoginComplete = OnLoginComplete;
        _onLogoff = OnLogoff;
        _onNameResolutionTick = OnNameResolutionTick;
    }

    /// <summary>True between the login edge and the matching logoff edge.</summary>
    public bool IsInWorld { get; private set; }

    public string CharacterName { get; private set; } = string.Empty;

    public string WorldName { get; private set; } = string.Empty;

    public string AccountName { get; private set; } = string.Empty;

    /// <summary>Raised once each time the session becomes in-world.</summary>
    public event Action? LoginComplete;

    /// <summary>
    /// Raised once each time the character/world/account names are all
    /// known — always at or after <see cref="LoginComplete"/>, never before.
    /// The host can report <see cref="IAutomationSurface.Character"/> with an
    /// empty <see cref="ICharacterInfo.Name"/> for a tick or two after
    /// <see cref="LoginComplete"/> fires (the player object exists in the
    /// object table before its name arrives), so every name-scoped owner —
    /// character-scoped command lists, tracker files, the chat logger, the
    /// inventory packer's profile lookup, the inventory logger — must start
    /// from this edge, never from <see cref="LoginComplete"/> directly.
    /// </summary>
    public event Action? SessionReady;

    /// <summary>Raised once each time an in-world session goes away.</summary>
    public event Action? Logoff;

    /// <summary>
    /// Hooks the host's real lifecycle events. Called from the plugin's
    /// <c>Enable()</c>.
    /// </summary>
    /// <remarks>
    /// If the automation surface already reports an in-world character at
    /// subscribe time (a Disable/Enable reload while never actually
    /// disconnected — a plugin reload, not a reconnect), this fires the login
    /// edge immediately rather than waiting for an event the host will never
    /// raise again for a session that never went away. That preserves the
    /// original's behaviour of reprinting the online banner whenever the
    /// plugin (re)starts against a session already in progress.
    /// </remarks>
    public void Subscribe()
    {
        if (_subscribed)
            return;

        _host.Events.LoginComplete += _onLoginComplete;
        _host.Events.Logoff += _onLogoff;
        _subscribed = true;

        IAutomationSurface automation = _host.Automation;
        if (automation.IsAvailable && automation.Character.IsInWorld)
            OnLoginComplete();
    }

    /// <summary>Unhooks the host's lifecycle events. Called from <c>Disable()</c>.</summary>
    public void Unsubscribe()
    {
        if (!_subscribed)
            return;

        _host.Events.LoginComplete -= _onLoginComplete;
        _host.Events.Logoff -= _onLogoff;
        StopNameResolution();
        _subscribed = false;
    }

    private void OnLoginComplete()
    {
        // A duplicate LoginComplete for a session already in-world (a
        // belt-and-suspenders host redelivery, for instance) must not
        // reprint the banner or re-run every LoginComplete subscriber's
        // one-time setup a second time for the same session.
        if (IsInWorld)
            return;

        ICharacterInfo character = _host.Automation.Character;
        IsInWorld = true;

        int population = character.ServerPopulation;
        _chat.Write(
            population >= 0
                ? "Plugin now online. Server population: "
                    + population.ToString(CultureInfo.InvariantCulture)
                : "Plugin now online.");

        LoginComplete?.Invoke();

        BeginNameResolution();
    }

    /// <summary>
    /// Tries to resolve all three names immediately; if the host hasn't
    /// populated them yet, arms a per-tick recheck instead of raising
    /// <see cref="SessionReady"/> with an empty segment.
    /// </summary>
    private void BeginNameResolution()
    {
        if (TryResolveNames())
        {
            SessionReady?.Invoke();
            return;
        }

        _nameResolutionPending = true;
        _host.Events.Tick += _onNameResolutionTick;
    }

    private void OnNameResolutionTick(double elapsedSeconds)
    {
        if (!TryResolveNames())
            return;

        StopNameResolution();
        SessionReady?.Invoke();
    }

    private bool TryResolveNames()
    {
        ICharacterInfo character = _host.Automation.Character;
        string name = character.Name;
        string worldName = character.WorldName;
        string accountName = character.AccountName;

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(worldName)
            || string.IsNullOrEmpty(accountName))
            return false;

        CharacterName = name;
        WorldName = worldName;
        AccountName = accountName;
        return true;
    }

    private void StopNameResolution()
    {
        if (!_nameResolutionPending)
            return;

        _host.Events.Tick -= _onNameResolutionTick;
        _nameResolutionPending = false;
    }

    private void OnLogoff()
    {
        // Mirrors the OnLoginComplete guard: a Logoff with no matching
        // in-world session must not re-fire the edge or re-run every
        // subscriber's teardown for a session that already ended.
        if (!IsInWorld)
            return;

        // The session ended before the name ever resolved: no name-scoped
        // owner ever started, so nothing was written under a wrong or empty
        // segment — but say so, since this is otherwise a silent no-op.
        if (_nameResolutionPending)
        {
            StopNameResolution();
            _host.Log.Warn(
                "Session ended before the character/world/account names "
                + "resolved; name-scoped state (tracker files, the chat "
                + "logger, the inventory logger, character-scoped command "
                + "lists) never started this session.");
        }

        IsInWorld = false;
        Logoff?.Invoke();
        CharacterName = string.Empty;
        WorldName = string.Empty;
        AccountName = string.Empty;
    }

    /// <summary>
    /// Clears the edge state and unsubscribes without raising
    /// <see cref="Logoff"/>. The plugin calls this from <c>Disable()</c> so a
    /// Disable/Enable cycle (a plugin reload with no underlying reconnect)
    /// starts from a clean slate and re-fires <see cref="LoginComplete"/> via
    /// <see cref="Subscribe"/>'s already-in-world check, instead of staying
    /// silent because <see cref="IsInWorld"/> was already true.
    /// </summary>
    public void Reset()
    {
        Unsubscribe();
        IsInWorld = false;
        CharacterName = string.Empty;
        WorldName = string.Empty;
        AccountName = string.Empty;
    }
}
