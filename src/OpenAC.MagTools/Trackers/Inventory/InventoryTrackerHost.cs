using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Trackers.ProfitLoss;

namespace OpenAC.MagTools.Trackers.Inventory;

/// <summary>
/// Shared lifecycle owner for §2.5 (consumables) and §2.6 (profit/loss): both
/// trackers share the exact same "start at LoginComplete, prime once, resync
/// on ObjectChanged and on a 500 ms timer, stop at Logoff" shape in the
/// original, so one host drives both from a single subscription set.
/// </summary>
public sealed class InventoryTrackerHost
{
    private static readonly TimeSpan ResyncInterval = TimeSpan.FromMilliseconds(500);

    private readonly IPluginHost _host;
    private Action<PluginObjectChange>? _onObjectChanged;
    private IDisposable? _timerRegistration;
    private bool _running;

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
        ResyncNow();

        _onObjectChanged = _ => ResyncNow();
        _host.Events.ObjectChanged += _onObjectChanged;

        _timerRegistration = scheduler.Every(ResyncInterval, ResyncNow);
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

        Consumables.Clear();
    }

    private void ResyncNow()
    {
        IReadOnlyList<PluginInventoryItem> owned = _host.Automation.Items.CaptureOwnedItems();
        Consumables.Resync(owned);
        ProfitLoss.Resync(owned);
    }
}
