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
    /// Queues one entry. If the queue was empty, starts the drain (a
    /// mid-drain enqueue rides the already-scheduled chain instead of
    /// double-scheduling).
    /// </summary>
    public void Enqueue(string? command)
    {
        bool wasEmpty = _queue.Count == 0;
        _queue.Enqueue(command);
        if (wasEmpty)
            ScheduleNext();
    }

    /// <summary>Drops every pending entry without dispatching it.</summary>
    public void Clear() => _queue.Clear();

    private void ScheduleNext()
    {
        if (_scheduler is null || _queue.Count == 0)
            return;
        _scheduler.RunOnNextTick(DrainOne);
    }

    private void DrainOne()
    {
        if (_queue.Count == 0)
            return;

        string? command = _queue.Dequeue();
        if (!string.IsNullOrEmpty(command))
            CommandDispatcher.Dispatch(_host, _router, command);

        ScheduleNext();
    }
}
