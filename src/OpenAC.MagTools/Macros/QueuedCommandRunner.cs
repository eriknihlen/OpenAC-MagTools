using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Commands;

namespace OpenAC.MagTools.Macros;

/// <summary>
/// Drains a FIFO queue of command strings one per tick, dispatching each
/// through <see cref="CommandDispatcher"/>. Shared by <see cref="LoginActions"/>
/// and <see cref="PeriodicCommands"/> so both honour the original's "one
/// command per RenderFrame" pacing with the same code -- the original ran a
/// think loop that dequeued and sent a single command every render frame so a
/// long command list could not flood the outgoing chat/command pipe in one
/// tick. A <c>null</c> entry is a dummy slot (the original enqueued one null
/// command first so the real commands run one frame after every other
/// plugin's own login handling); dequeuing one just advances the queue.
/// </summary>
internal sealed class QueuedCommandRunner
{
    private readonly IPluginHost _host;
    private readonly MtCommandRouter _router;
    private readonly Queue<string?> _queue = new();
    private TickScheduler? _scheduler;

    /// <summary>
    /// True while a drain callback is armed (already scheduled, or about to
    /// reschedule itself). Guards <see cref="Enqueue"/> against arming a
    /// SECOND concurrent chain -- without it, a <see cref="Clear"/>
    /// immediately followed by fresh <see cref="Enqueue"/> calls (a
    /// Stop-then-Run in one synthetic frame, e.g. a reconnect's
    /// Logoff-then-LoginComplete) would see the queue as empty again and
    /// arm a second chain alongside the still-pending first one; both would
    /// then fire on the same tick, permanently doubling the dispatch rate
    /// for the rest of that queue's life (L1, 2026-09-16 review). This flag
    /// is deliberately NOT reset by <see cref="Clear"/> -- the callback that
    /// was already scheduled is still going to run, and it is the one that
    /// clears the flag, whether it finds the queue newly repopulated (and
    /// keeps draining) or still empty (and stops).
    /// </summary>
    private bool _draining;

    public QueuedCommandRunner(IPluginHost host, MtCommandRouter router)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(router);
        _host = host;
        _router = router;
    }

    public int Count => _queue.Count;

    /// <summary>The scheduler each drain step is re-armed on. Rebind on every session start.</summary>
    public void Bind(TickScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
    }

    /// <summary>
    /// Queues one entry. If no drain chain is currently armed, arms one (a
    /// mid-drain enqueue rides the already-armed chain instead of
    /// double-arming).
    /// </summary>
    public void Enqueue(string? command)
    {
        _queue.Enqueue(command);
        if (!_draining)
        {
            _draining = true;
            ScheduleNext();
        }
    }

    /// <summary>
    /// Drops every pending entry without dispatching it. Deliberately leaves
    /// <see cref="_draining"/> untouched -- see its remarks.
    /// </summary>
    public void Clear() => _queue.Clear();

    private void ScheduleNext()
    {
        if (_scheduler is null)
        {
            _draining = false;
            return;
        }
        _scheduler.RunOnNextTick(DrainOne);
    }

    private void DrainOne()
    {
        if (_queue.Count == 0)
        {
            _draining = false;
            return;
        }

        string? command = _queue.Dequeue();
        if (!string.IsNullOrEmpty(command))
            CommandDispatcher.Dispatch(_host, _router, command);

        if (_queue.Count == 0)
            _draining = false;
        else
            ScheduleNext();
    }
}
