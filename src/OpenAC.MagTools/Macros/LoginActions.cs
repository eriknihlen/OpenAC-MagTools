using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Commands;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Macros;

/// <summary>
/// Port of the original's §1.14 <c>LoginActions</c> (<c>Macros/LoginActions.cs</c>):
/// runs the stored On-Login and On-Login-Complete command lists, character
/// scope before server scope, one command per tick, behind a leading dummy
/// slot.
/// </summary>
/// <remarks>
/// <para>
/// DELIBERATE DEVIATION: the original hung two separate handlers off two
/// separate edges (<c>CharacterFilter.Login</c> and the plugin's own
/// <c>LoginComplete</c>), each enqueueing its own dummy-first list. OpenAC's
/// host contract has only one edge -- <see cref="SessionContext.LoginComplete"/>,
/// which already fires no earlier than the client's own login handling has
/// settled -- so this port plays BOTH lists on that single edge: one dummy
/// slot, then the On-Login list, then the On-Login-Complete list, in that
/// order, still one command per tick. See <c>docs/deviations.md</c>.
/// </para>
/// <para>
/// Dispatch is <see cref="CommandDispatcher"/>: a <c>/mt…</c> entry goes to
/// the console, anything else is submitted to chat.
/// </para>
/// </remarks>
public sealed class LoginActions
{
    private readonly QueuedCommandRunner _runner;
    private readonly ScopedCommandStore _store;

    public LoginActions(IPluginHost host, MtCommandRouter router, ScopedCommandStore store)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(store);
        _runner = new QueuedCommandRunner(host, router);
        _store = store;
    }

    /// <summary>How many entries are still queued -- exposed for tests.</summary>
    internal int PendingCount => _runner.Count;

    /// <summary>
    /// Runs once per <see cref="SessionContext.LoginComplete"/> edge. Replaces
    /// whatever this instance had queued (a reconnect that lands a second
    /// <c>LoginComplete</c> before the previous session's queue drained
    /// starts fresh, matching a real login always beginning from an empty
    /// queue).
    /// </summary>
    public void Run(TickScheduler scheduler, string characterScopePath, string serverScopePath)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(characterScopePath);
        ArgumentNullException.ThrowIfNull(serverScopePath);

        _runner.Clear();
        _runner.Bind(scheduler);

        // The dummy slot: the real commands run one tick after this method
        // returns, mirroring the original's "let every other plugin finish
        // its own Login handling first".
        _runner.Enqueue(null);

        foreach (string command in _store.GetOnLoginCommands(characterScopePath))
            _runner.Enqueue(command);
        foreach (string command in _store.GetOnLoginCommands(serverScopePath))
            _runner.Enqueue(command);

        foreach (string command in _store.GetOnLoginCompleteCommands(characterScopePath))
            _runner.Enqueue(command);
        foreach (string command in _store.GetOnLoginCompleteCommands(serverScopePath))
            _runner.Enqueue(command);
    }

    /// <summary>Drops whatever is still queued. Called on logoff/disable.</summary>
    public void Stop() => _runner.Clear();
}
