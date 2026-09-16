using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Trackers.ProfitLoss;

/// <summary>
/// Port of the original's §2.6 profit/loss tracker: four 60-minute
/// <see cref="ValueSnapShotGroup"/> series of total pyreal value — Peas,
/// Comps, Salvage, and Net Profit — recomputed from the whole owned-item set
/// whenever a matching-class object is created, changed, or released.
/// </summary>
/// <remarks>
/// Same "resync the whole set instead of diffing one object" simplification
/// as <see cref="Inventory.ConsumablesTracker"/> — see its remarks and
/// docs/deviations.md.
/// </remarks>
public sealed class ProfitLossTracker
{
    /// <summary>The MMD/h divisor — one million dollars' worth of pyreals, informally.</summary>
    public const double MmdDivisor = 250000d;

    private const int RetentionMinutes = 60;

    public ProfitLossTracker(TimeProvider? timeProvider = null)
    {
        Peas = new ValueSnapShotGroup(RetentionMinutes, timeProvider);
        Comps = new ValueSnapShotGroup(RetentionMinutes, timeProvider);
        Salvage = new ValueSnapShotGroup(RetentionMinutes, timeProvider);
        NetProfit = new ValueSnapShotGroup(RetentionMinutes, timeProvider);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private readonly TimeProvider _timeProvider;

    public event Action? Changed;

    public ValueSnapShotGroup Peas { get; }

    public ValueSnapShotGroup Comps { get; }

    public ValueSnapShotGroup Salvage { get; }

    public ValueSnapShotGroup NetProfit { get; }

    public void Resync(IReadOnlyList<PluginInventoryItem> ownedItems)
    {
        ArgumentNullException.ThrowIfNull(ownedItems);

        int peas = 0;
        int comps = 0;
        int salvage = 0;
        int netProfit = 0;

        foreach (PluginInventoryItem item in ownedItems)
        {
            switch (item.ObjectClass)
            {
                case PluginObjectClass.SpellComponent:
                    if (item.Name.Contains("Pea", StringComparison.Ordinal))
                        peas += item.Value;
                    else
                        comps += item.Value;
                    netProfit += item.Value;
                    break;
                case PluginObjectClass.Salvage:
                    salvage += item.Value;
                    netProfit += item.Value;
                    break;
                case PluginObjectClass.TradeNote:
                case PluginObjectClass.Money:
                    netProfit += item.Value;
                    break;
            }
        }

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        // Unlike ConsumablesTracker's per-NAME groups, these four series
        // always exist and are always recomputed from the full owned set, so
        // every Resync stamps all four — a legitimate "0 -> real value"
        // transition (say, a fresh baseline immediately followed by an hour
        // of real profit) must still leave a genuine zero snapshot behind for
        // the rate math to measure against. H1's login-fabrication fix lives
        // in InventoryTrackerHost's priming-on-quiescence gate, which governs
        // WHEN this method is called at all, not whether an individual call
        // records data.
        Peas.AddSnapShot(now, peas, RetentionMinutes);
        Comps.AddSnapShot(now, comps, RetentionMinutes);
        Salvage.AddSnapShot(now, salvage, RetentionMinutes);
        NetProfit.AddSnapShot(now, netProfit, RetentionMinutes);

        Changed?.Invoke();
    }

    /// <summary>
    /// Raises <see cref="Changed"/> without touching any history — the idle
    /// half of the H1 coalescing tick in
    /// <see cref="Inventory.InventoryTrackerHost"/>.
    /// </summary>
    public void RaiseChanged() => Changed?.Invoke();

    /// <summary>MMD/h over <paramref name="historyPeriod"/>, projected to a one-hour rate.</summary>
    public static double MmdPerHour(ValueSnapShotGroup series, TimeSpan historyPeriod)
        => series.GetValueDifference(historyPeriod, TimeSpan.FromHours(1)) / MmdDivisor;
}
