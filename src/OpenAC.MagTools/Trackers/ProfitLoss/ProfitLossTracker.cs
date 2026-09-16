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
        // always exist. P5 MEDIUM-2 (second fix round): stamp a series only
        // when it has never recorded a single snapshot yet (SnapShotCount ==
        // 0 — so a genuine "0" baseline is still recorded once, which the
        // rate math needs to measure a legitimate "0 -> real value"
        // transition against; see ProfitLossTrackerTests.
        // MmdPerHour_divides_the_rate_by_250000) OR its recomputed total
        // actually differs from what it last recorded. This is the SAME
        // "only stamp on an actual change" rule ConsumablesTracker.Resync
        // already applies per group, now safe for these four fixed series
        // too because the SnapShotCount == 0 carve-out keeps the very first
        // call's zero baseline intact regardless of whether the value is
        // also zero. H1's login-fabrication fix lives in
        // InventoryTrackerHost's priming-on-quiescence gate, which governs
        // WHEN this method is called at all; this governs whether an
        // individual call, once it IS called, actually records anything.
        StampIfChanged(Peas, peas, now);
        StampIfChanged(Comps, comps, now);
        StampIfChanged(Salvage, salvage, now);
        StampIfChanged(NetProfit, netProfit, now);

        Changed?.Invoke();
    }

    private static void StampIfChanged(ValueSnapShotGroup series, int value, DateTime now)
    {
        if (series.SnapShotCount == 0 || series.LastKnownValue != value)
            series.AddSnapShot(now, value, RetentionMinutes);
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
