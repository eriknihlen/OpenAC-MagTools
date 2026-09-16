using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Trackers.ProfitLoss;

namespace OpenAC.MagTools.Trackers.Inventory;

/// <summary>
/// Shared lifecycle owner for §2.5 (consumables) and §2.6 (profit/loss): both
/// trackers share the exact same "start at LoginComplete, prime once, resync
/// on ObjectChanged and on a 500 ms timer, stop at Logoff" shape in the
/// original, so one host drives both from a single subscription set.
/// </summary>
/// <remarks>
/// H1 (P5 fix round): priming used to happen synchronously inside
/// <see cref="Start"/>, before the host's async post-login inventory stream
/// (a burst of <see cref="IEvents.ObjectChanged"/> events) had delivered
/// anything — unlike the original, which ran at Decal's <c>LoginComplete</c>
/// with <c>GetInventory()</c> already fully populated. That baseline snapshot
/// recorded near-zero totals, and every subsequent burst event then recorded
/// a real (fast-growing) total a fraction of a second later — the resulting
/// "went from ~0 to full inventory in under a second" delta, extrapolated to
/// an hourly rate by <see cref="Trackers.ValueSnapShotGroup.GetValueDifference"/>,
/// fabricated numbers like an 893.0/h Net Profit rate and equally fake
/// consumable depletion averages at every login. This host now waits for the
/// inventory stream to go quiet for <see cref="PrimeQuietPeriod"/> before
/// taking that first snapshot, and coalesces every <c>ObjectChanged</c> that
/// arrives inside one 500 ms tick into at most one
/// <c>CaptureOwnedItems()</c> capture — an idle tick only re-raises
/// <see cref="ConsumablesTracker.Changed"/>/<see cref="ProfitLossTracker.Changed"/>
/// so bound UI still refreshes its own time-window math. See docs/deviations.md.
/// </remarks>
public sealed class InventoryTrackerHost
{
    private static readonly TimeSpan ResyncInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How long the inventory stream must go quiet (no
    /// <see cref="IEvents.ObjectChanged"/> at all) after <see cref="Start"/>
    /// before the FIRST snapshot is taken — see this class's remarks (H1).
    /// </summary>
    private static readonly TimeSpan PrimeQuietPeriod = TimeSpan.FromSeconds(1);

    private readonly IPluginHost _host;
    private Action<PluginObjectChange>? _onObjectChanged;
    private IDisposable? _timerRegistration;
    private TickScheduler? _scheduler;
    private bool _running;
    private bool _primed;
    private bool _dirty;
    private double _lastActivityElapsedSeconds;

    public InventoryTrackerHost(IPluginHost host, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        Consumables = new ConsumablesTracker(timeProvider: timeProvider);
        ProfitLoss = new ProfitLossTracker(timeProvider);
    }

    public ConsumablesTracker Consumables { get; }

    public ProfitLossTracker ProfitLoss { get; }

    public void Start(TickScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (_running)
            return;

        _running = true;
        _scheduler = scheduler;
        _primed = false;
        _dirty = false;
        _lastActivityElapsedSeconds = scheduler.ElapsedSeconds;

        _onObjectChanged = _ =>
        {
            _lastActivityElapsedSeconds = _scheduler!.ElapsedSeconds;
            _dirty = true;
        };
        _host.Events.ObjectChanged += _onObjectChanged;

        _timerRegistration = scheduler.Every(ResyncInterval, OnTick);
    }

    public void Stop()
    {
        if (!_running)
            return;

        _running = false;

        if (_onObjectChanged is not null)
            _host.Events.ObjectChanged -= _onObjectChanged;
        _onObjectChanged = null;

        _timerRegistration?.Dispose();
        _timerRegistration = null;
        _scheduler = null;
        _primed = false;
        _dirty = false;

        Consumables.Clear();
    }

    /// <summary>
    /// The 500 ms tick. Before priming, this only checks whether the
    /// inventory stream has gone quiet yet — see the class remarks (H1).
    /// After priming, a capture only happens when an <c>ObjectChanged</c>
    /// arrived since the last tick (coalesced to one capture no matter how
    /// many arrived); an idle tick only re-raises <c>Changed</c>.
    /// </summary>
    private void OnTick()
    {
        if (_scheduler is null)
            return;

        if (!_primed)
        {
            if (_scheduler.ElapsedSeconds - _lastActivityElapsedSeconds < PrimeQuietPeriod.TotalSeconds)
                return;

            _primed = true;
            _dirty = false;
            ResyncNow();
            return;
        }

        if (_dirty)
        {
            _dirty = false;
            ResyncNow();
        }
        else
        {
            Consumables.RaiseChanged();
            ProfitLoss.RaiseChanged();
        }
    }

    private void ResyncNow()
    {
        IReadOnlyList<PluginInventoryItem> owned = _host.Automation.Items.CaptureOwnedItems();
        Consumables.Resync(owned);
        ProfitLoss.Resync(owned);
    }
}
