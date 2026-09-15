using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools;

/// <summary>
/// The plugin's only clock. <see cref="IEvents.Tick"/> is the host's sole timer,
/// so every think loop, delay and deferred action in the port hangs off this
/// one owner rather than each feature subscribing separately.
/// </summary>
/// <remarks>
/// A callback that throws is reported the way the original reported an
/// unhandled plugin exception and then left alone; one bad think loop never
/// stops the others.
/// </remarks>
public sealed class TickScheduler : IDisposable
{
    private readonly IEvents _events;
    private readonly ChatOutput _chat;
    private readonly Action<double> _handler;
    private readonly List<Entry> _entries = [];
    private readonly Queue<Action> _nextTick = new();
    private bool _disposed;

    public TickScheduler(IEvents events, ChatOutput chat)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(chat);
        _events = events;
        _chat = chat;
        _handler = Tick;
        _events.Tick += _handler;
    }

    /// <summary>Seconds accumulated since construction.</summary>
    public double ElapsedSeconds { get; private set; }

    /// <summary>
    /// Runs <paramref name="callback"/> no more often than once per
    /// <paramref name="interval"/>. Disposing the result stops it.
    /// </summary>
    public IDisposable Every(TimeSpan interval, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                "A repeating callback needs a positive interval.");

        return Add(new Entry(this, interval, callback, repeat: true));
    }

    /// <summary>Runs <paramref name="callback"/> once after <paramref name="delay"/>.</summary>
    public IDisposable Delay(TimeSpan delay, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (delay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(delay),
                "A delayed callback needs a non-negative delay.");

        return Add(new Entry(this, delay, callback, repeat: false));
    }

    /// <summary>Queues <paramref name="action"/> for the next tick.</summary>
    public void RunOnNextTick(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _nextTick.Enqueue(action);
    }

    /// <summary>
    /// Advances the scheduler. The host calls this through
    /// <see cref="IEvents.Tick"/>; tests call it directly.
    /// </summary>
    public void Tick(double elapsedSeconds)
    {
        if (_disposed)
            return;
        if (double.IsNaN(elapsedSeconds) || elapsedSeconds < 0d)
            elapsedSeconds = 0d;

        ElapsedSeconds += elapsedSeconds;

        // Snapshot both collections: a callback may queue more work or cancel
        // an entry, and neither may disturb the pass in flight.
        int queued = _nextTick.Count;
        for (int index = 0; index < queued; index++)
            Invoke(_nextTick.Dequeue());

        Entry[] due = [.. _entries];
        foreach (Entry entry in due)
        {
            if (entry.Cancelled)
                continue;
            entry.Accumulated += elapsedSeconds;
            if (entry.Accumulated < entry.Interval.TotalSeconds)
                continue;

            if (entry.Repeat)
                entry.Accumulated = 0d;
            else
                entry.Cancel();

            Invoke(entry.Callback);
        }

        _entries.RemoveAll(static entry => entry.Cancelled);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _events.Tick -= _handler;
        _entries.Clear();
        _nextTick.Clear();
    }

    private IDisposable Add(Entry entry)
    {
        _entries.Add(entry);
        return entry;
    }

    private void Invoke(Action callback)
    {
        try
        {
            callback();
        }
        catch (Exception exception)
        {
            _chat.WriteException(exception);
        }
    }

    private sealed class Entry(
        TickScheduler owner,
        TimeSpan interval,
        Action callback,
        bool repeat) : IDisposable
    {
        public TimeSpan Interval { get; } = interval;
        public Action Callback { get; } = callback;
        public bool Repeat { get; } = repeat;
        public double Accumulated { get; set; }
        public bool Cancelled { get; private set; }

        public void Cancel() => Cancelled = true;

        public void Dispose()
        {
            Cancelled = true;
            _ = owner;
        }
    }
}
