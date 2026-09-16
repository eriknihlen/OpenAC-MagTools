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
/// <para>
/// Matches the original exactly (fixed at the 2026-09-16 review, M1):
/// <c>minutesAfterMidnight</c> reads the LOCAL wall clock
/// (<see cref="TimeProvider.GetLocalNow"/>, the port's equivalent of the
/// original's <c>DateTime.Now</c>), while the same-minute guard reads only
/// the UTC minute-of-hour (<c>GetUtcNow().UtcDateTime.Minute</c>, the port's
/// equivalent of the original's own <c>DateTime.UtcNow.Minute</c> guard) --
/// two DIFFERENT clocks, exactly as upstream mixed them.
/// </para>
/// <para>
/// KNOWN UPSTREAM QUIRK, faithfully reproduced (not a port bug -- see L5 in
/// <c>docs/deviations.md</c>): because the guard only compares a 0-59
/// minute-of-hour value, not a full timestamp, two evaluations that land on
/// the same minute-of-hour more than an hour apart (only possible if a gap
/// in polling skips a whole hour, which the 20-second timer cadence never
/// does during a live session) would be treated as "already evaluated" and
/// skipped. This is the original's own behavior; the port does not correct
/// it.
/// </para>
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

    /// <summary>The UTC minute-of-hour (0-59) last evaluated -- the original's "same minute never runs twice" guard.</summary>
    private int? _lastEvaluatedUtcMinute;

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
        _lastEvaluatedUtcMinute = null;
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
        _lastEvaluatedUtcMinute = null;
    }

    /// <summary>Runs one 20-second tick of the timer. Internal so a test can drive it directly at a fixed clock instead of stepping the scheduler 3 times.</summary>
    internal void OnTimer()
    {
        int utcMinute = _timeProvider.GetUtcNow().UtcDateTime.Minute;
        if (_lastEvaluatedUtcMinute == utcMinute)
            return;
        _lastEvaluatedUtcMinute = utcMinute;

        DateTime localNow = _timeProvider.GetLocalNow().DateTime;
        int minutesAfterMidnight = (int)(localNow - localNow.Date).TotalMinutes;

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
