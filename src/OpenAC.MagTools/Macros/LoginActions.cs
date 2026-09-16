using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Commands;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Macros;

/// <summary>
/// Port of the original's §1.14 <c>LoginActions</c> (<c>Macros/LoginActions.cs</c>):
/// runs the stored On-Login and On-Login-Complete command lists, one command
/// per tick, behind a leading dummy slot.
/// </summary>
/// <remarks>
/// <para>
/// DELIBERATE DEVIATION: the original hung two separate handlers off two
/// separate edges (<c>CharacterFilter.Login</c> and the plugin's own
/// <c>LoginComplete</c>), each enqueueing its own dummy-first list. OpenAC's
/// host contract has only one edge with the character's identity guaranteed
/// -- <see cref="SessionContext.SessionReady"/> -- but the SERVER scope
/// needs only <see cref="ICharacterInfo.WorldName"/>, which (unlike
/// <see cref="ICharacterInfo.Name"/>) the host reports correctly at the
/// earlier <see cref="SessionContext.LoginComplete"/> edge. So this port
/// runs the two scopes' lists on their own independent queues, each starting
/// as soon as its own scope is known: <see cref="RunServerScope"/> from
/// <c>LoginComplete</c>, <see cref="RunCharacterScope"/> from
/// <c>SessionReady</c>. Each queue keeps the original's own dummy-first,
/// On-Login-before-On-Login-Complete ordering; the CROSS-scope
/// character-before-server ordering the original had (both scopes known at
/// once, in the same call) is not preserved when the two edges land on
/// different ticks -- an unavoidable consequence of gating character-scoped
/// work on the character name actually resolving. See MEDIUM-4 in the P9 fix
/// round and <c>docs/deviations.md</c>.
/// </para>
/// <para>
/// Dispatch is <see cref="CommandDispatcher"/>: a <c>/mt…</c> entry goes to
/// the console, anything else is submitted to chat.
/// </para>
/// </remarks>
public sealed class LoginActions
{
    private readonly QueuedCommandRunner _serverRunner;
    private readonly QueuedCommandRunner _characterRunner;
    private readonly ScopedCommandStore _store;

    public LoginActions(IPluginHost host, MtCommandRouter router, ScopedCommandStore store)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(store);
        _serverRunner = new QueuedCommandRunner(host, router);
        _characterRunner = new QueuedCommandRunner(host, router);
        _store = store;
    }

    /// <summary>How many entries are still queued across both scopes -- exposed for tests.</summary>
    internal int PendingCount => _serverRunner.Count + _characterRunner.Count;

    /// <summary>
    /// Runs once per <see cref="SessionContext.LoginComplete"/> edge, for the
    /// server-scoped lists only. Replaces whatever this instance had queued
    /// for the server scope (a reconnect that lands a second
    /// <c>LoginComplete</c> before the previous session's queue drained
    /// starts fresh, matching a real login always beginning from an empty
    /// queue).
    /// </summary>
    public void RunServerScope(TickScheduler scheduler, string serverScopePath)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(serverScopePath);

        _serverRunner.Clear();
        _serverRunner.Bind(scheduler);

        // The dummy slot: the real commands run one tick after this method
        // returns, mirroring the original's "let every other plugin finish
        // its own Login handling first".
        _serverRunner.Enqueue(null);

        foreach (string command in _store.GetOnLoginCommands(serverScopePath))
            _serverRunner.Enqueue(command);
        foreach (string command in _store.GetOnLoginCompleteCommands(serverScopePath))
            _serverRunner.Enqueue(command);
    }

    /// <summary>
    /// Runs once per <see cref="SessionContext.SessionReady"/> edge, for the
    /// character-scoped lists only. Independent queue/dummy slot from
    /// <see cref="RunServerScope"/> -- see this type's remarks.
    /// </summary>
    public void RunCharacterScope(TickScheduler scheduler, string characterScopePath)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(characterScopePath);

        _characterRunner.Clear();
        _characterRunner.Bind(scheduler);
        _characterRunner.Enqueue(null);

        foreach (string command in _store.GetOnLoginCommands(characterScopePath))
            _characterRunner.Enqueue(command);
        foreach (string command in _store.GetOnLoginCompleteCommands(characterScopePath))
            _characterRunner.Enqueue(command);
    }

    /// <summary>Drops whatever is still queued in both scopes. Called on logoff/disable.</summary>
    public void Stop()
    {
        _serverRunner.Clear();
        _characterRunner.Clear();
    }
}
