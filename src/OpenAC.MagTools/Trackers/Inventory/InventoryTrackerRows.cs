using System.Globalization;
using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Trackers.ProfitLoss;

namespace OpenAC.MagTools.Trackers.Inventory;

/// <summary>
/// Builds the Trackers → Inv. Items list's rows exactly the way the
/// original's <c>InventoryTrackerGUI</c> laid the runtime list out: a fixed
/// 7-row profit block/header preamble followed by the tracked consumables,
/// sorted by class z-index then by descending unit value.
/// </summary>
public static class InventoryTrackerRows
{
    public readonly record struct Row(
        uint Icon,
        string Name,
        string Count,
        string Average5m,
        string Average1h,
        string Hours);

    /// <summary>
    /// <c>SpellComponent 0, ManaStone 1, HealingKit 2, TradeNote 3, Salvage 4</c>,
    /// everything else last.
    /// </summary>
    private static int ClassZIndex(PluginObjectClass objectClass) => objectClass switch
    {
        PluginObjectClass.SpellComponent => 0,
        PluginObjectClass.ManaStone => 1,
        PluginObjectClass.HealingKit => 2,
        PluginObjectClass.TradeNote => 3,
        PluginObjectClass.Salvage => 4,
        _ => 5,
    };

    public static IReadOnlyList<Row> Build(InventoryTrackerHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var rows = new List<Row>(8 + host.Consumables.Tracked.Count)
        {
            // Row 0: profit-block header.
            new(0u, string.Empty, string.Empty, "MMD/h ~5m", "MMD/h ~1h", string.Empty),

            ProfitRow("Peas", host.ProfitLoss.Peas),
            ProfitRow("Comps", host.ProfitLoss.Comps),
            ProfitRow("Salvage", host.ProfitLoss.Salvage),
            ProfitRow("Net Profit", host.ProfitLoss.NetProfit),

            // Row 5: blank separator.
            new(0u, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty),

            // Row 6: consumables header.
            new(0u, string.Empty, "Count", "Avg/h ~5m", "Avg/h ~1h", "(Hrs)"),
        };

        var consumables = new List<TrackedConsumable>(host.Consumables.Tracked);
        consumables.Sort(static (a, b) =>
        {
            int classOrder = ClassZIndex(a.ObjectClass).CompareTo(ClassZIndex(b.ObjectClass));
            return classOrder != 0 ? classOrder : b.ItemValue.CompareTo(a.ItemValue);
        });

        foreach (TrackedConsumable tracked in consumables)
            rows.Add(ConsumableRow(tracked));

        return rows;
    }

    /// <summary>M4: blank, not "0.0", when a rate is exactly zero — matches the original's own ternary.</summary>
    private static Row ProfitRow(string name, ValueSnapShotGroup series)
    {
        double average5m = ProfitLossTracker.MmdPerHour(series, TimeSpan.FromMinutes(5));
        double average1h = ProfitLossTracker.MmdPerHour(series, TimeSpan.FromHours(1));
        return new Row(
            0u, name, string.Empty,
            average5m == 0d ? string.Empty : average5m.ToString("N1", CultureInfo.InvariantCulture),
            average1h == 0d ? string.Empty : average1h.ToString("N1", CultureInfo.InvariantCulture),
            string.Empty);
    }

    /// <summary>
    /// M4: matches the original's <c>UpdateInventoryItem</c> exactly — when
    /// <c>LastKnownValue == 0</c>, ALL THREE of Avg/h~5m, Avg/h~1h and (Hrs)
    /// go blank together (the Count cell itself still reads "0"); otherwise
    /// each of the three is blanked independently when it individually
    /// computes to zero.
    /// </summary>
    private static Row ConsumableRow(TrackedConsumable tracked)
    {
        int lastKnown = tracked.CurrentCount;
        string countText = lastKnown.ToString(CultureInfo.InvariantCulture);

        if (lastKnown == 0)
            return new Row(tracked.Icon, tracked.Name, countText, string.Empty, string.Empty, string.Empty);

        double average5m = tracked.History.GetValueDifference(
            TimeSpan.FromMinutes(5), TimeSpan.FromHours(1));
        double average1h = tracked.History.GetValueDifference(
            TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        TimeSpan depletion = tracked.History.GetTimeToDepletion(TimeSpan.FromHours(1));
        string hoursText = depletion <= TimeSpan.Zero || depletion == TimeSpan.MaxValue
            ? string.Empty
            : depletion.TotalHours.ToString("N1", CultureInfo.InvariantCulture);

        return new Row(
            tracked.Icon,
            tracked.Name,
            countText,
            average5m == 0d ? string.Empty : average5m.ToString("N1", CultureInfo.InvariantCulture),
            average1h == 0d ? string.Empty : average1h.ToString("N1", CultureInfo.InvariantCulture),
            hoursText);
    }
}
