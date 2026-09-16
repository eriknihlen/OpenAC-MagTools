using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Commands;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Macros;

/// <summary>
/// Port of the original's §1.14 <c>PeriodicCommands</c>
/// (<c>Macros/PeriodicCommands.cs</c>): a 20-second timer starting at
/// <c>LoginComplete</c> ("3 times a minute") that, once per distinct
/// wall-clock minute, fires every stored periodic command whose
/// <c>(minutesAfterMidnight + offset) % interval == 0</c>, draining matches
/// one per tick through the same <c>/mt</c>-vs-chat dispatch rule as
/// <see cref="LoginActions"/>. Character-scope commands are evaluated before
/// server-scope commands.
/// </summary>
/// <remarks>
/// DELIBERATE DEVIATION: the original read <c>DateTime.Now</c> -- the
/// retail client process's OS-local wall clock. This port reads
/// <see cref="TimeProvider.GetUtcNow"/> instead, matching every other timer
/// in this codebase (<c>AutoTradeAccept</c>, <c>Looter</c>,
/// <c>ManaRecharger</c>, …), rather than <c>GetLocalNow()</c>. A headless
/// session has no meaningful "the player's desktop time zone" to read, and
/// a UTC-based minute-of-day keeps the same-minute guard and the
/// interval/offset match deterministic and testable at a fixed clock. The
/// practical effect for a graphical session is that "minutes after
/// midnight" is midnight UTC, not the player's local midnight -- a periodic
/// command still fires exactly once every <c>interval</c> minutes, just on
/// a UTC-aligned grid instead of a local one. See <c>docs/deviations.md</c>.
/// </remarks>
public sealed class PeriodicCommands
{
    private static readonly TimeSpan TimerInterval = TimeSpan.FromSeconds(20);

    private readonly QueuedCommandRunner _runner;
    private readonly ScopedCommandStore _store;
    private readonly TimeProvider _timeProvider;

    private string _characterScopePath = string.Empty;
    private string _serverScopePath = string.Empty;
    private IDisposable? _timerRegistration;

    /// <summary>The wall-clock minute last evaluated -- the original's "same minute never runs twice" guard.</summary>
    private DateTime? _lastEvaluatedMinute;

    public PeriodicCommands(
        IPluginHost host,
        MtCommandRouter router,
        ScopedCommandStore store,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(store);
        _runner = new QueuedCommandRunner(host, router);
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>How many matched commands are still queued -- exposed for tests.</summary>
    internal int PendingCount => _runner.Count;

    /// <summary>Starts the 20-second timer for one session. Safe to call again on a reconnect -- replaces the previous timer and guard state.</summary>
    public void Start(TickScheduler scheduler, string characterScopePath, string serverScopePath)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(characterScopePath);
        ArgumentNullException.ThrowIfNull(serverScopePath);

        _characterScopePath = characterScopePath;
        _serverScopePath = serverScopePath;
        _lastEvaluatedMinute = null;
        _runner.Clear();
        _runner.Bind(scheduler);

        _timerRegistration?.Dispose();
        _timerRegistration = scheduler.Every(TimerInterval, OnTimer);
    }

    /// <summary>Stops the timer and drops anything still queued. Called on logoff/disable.</summary>
    public void Stop()
    {
        _timerRegistration?.Dispose();
        _timerRegistration = null;
        _runner.Clear();
        _characterScopePath = string.Empty;
        _serverScopePath = string.Empty;
        _lastEvaluatedMinute = null;
    }

    /// <summary>Runs one 20-second tick of the timer. Internal so a test can drive it directly at a fixed clock instead of stepping the scheduler 3 times.</summary>
    internal void OnTimer()
    {
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        var minuteBucket = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
        if (_lastEvaluatedMinute == minuteBucket)
            return;
        _lastEvaluatedMinute = minuteBucket;

        int minutesAfterMidnight = (int)(now - now.Date).TotalMinutes;

        EnqueueMatching(_store.GetPeriodicCommands(_characterScopePath), minutesAfterMidnight);
        EnqueueMatching(_store.GetPeriodicCommands(_serverScopePath), minutesAfterMidnight);
    }

    private void EnqueueMatching(IReadOnlyList<PeriodicCommand> commands, int minutesAfterMidnight)
    {
        foreach (PeriodicCommand command in commands)
        {
            int interval = (int)command.Interval.TotalMinutes;
            if (interval <= 0)
                continue;
            int offset = (int)command.OffsetFromMidnight.TotalMinutes;

            int remainder = (minutesAfterMidnight + offset) % interval;
            if (remainder < 0)
                remainder += interval;
            if (remainder == 0)
                _runner.Enqueue(command.Command);
        }
    }
}
