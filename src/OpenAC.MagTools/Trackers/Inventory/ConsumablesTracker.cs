using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Trackers.Inventory;

/// <summary>
/// Port of the original's §2.5 inventory (consumables) tracker: watches the
/// whole inventory for spell components (Tapers/Scarabs by name), mana
/// stones, healing kits, trade notes, salvage, and the two corrupted-essence
/// misc items, and keeps a 60-minute count history per tracked name (or
/// synthetic group name) for the depletion-rate math.
/// </summary>
/// <remarks>
/// The original re-scanned per <c>Create</c>/<c>Change</c>/<c>Release</c>
/// object event and additionally re-raised <c>ItemChanged</c> off a 500 ms
/// timer, rate-limited to once per item per second. This port folds both into
/// a single <see cref="Resync"/> the host calls from both an
/// <see cref="IEvents.ObjectChanged"/> handler and a 500 ms scheduler tick,
/// and rate-limits the resulting <see cref="Changed"/> event as a whole
/// (once per second) rather than per tracked name — our binding layer polls
/// property getters on any <see cref="Changed"/> rather than per-row, so a
/// coarser rate limit has the same observable effect. See docs/deviations.md.
/// </remarks>
public sealed class ConsumablesTracker(int minutesToRetain = 60, TimeProvider? timeProvider = null)
{
    private readonly Dictionary<string, TrackedConsumable> _tracked =
        new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public event Action? Changed;

    public IReadOnlyCollection<TrackedConsumable> Tracked => _tracked.Values;

    public void Clear()
    {
        if (_tracked.Count == 0)
            return;
        _tracked.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// Classifies one owned item into its tracked group name, or null when it
    /// is not a tracked class at all.
    /// </summary>
    public static string? Classify(in PluginInventoryItem item)
    {
        switch (item.ObjectClass)
        {
            case PluginObjectClass.SpellComponent:
                return item.Name.Contains("Taper", StringComparison.Ordinal)
                    || item.Name.Contains("Scarab", StringComparison.Ordinal)
                        ? item.Name
                        : null;
            case PluginObjectClass.ManaStone:
            case PluginObjectClass.HealingKit:
                return item.Name;
            case PluginObjectClass.TradeNote:
                return "Trade Note";
            case PluginObjectClass.Salvage:
                return "Salvage";
            case PluginObjectClass.Misc:
                return item.Name is "Corrupted Essence" or "Lesser Corrupted Essence"
                    ? "Corrupted Essence"
                    : null;
            default:
                return null;
        }
    }

    /// <summary>Rescans the whole owned-item set and appends one snapshot per tracked name.</summary>
    public void Resync(IReadOnlyList<PluginInventoryItem> ownedItems)
    {
        ArgumentNullException.ThrowIfNull(ownedItems);

        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        var representative = new Dictionary<string, PluginInventoryItem>(StringComparer.Ordinal);

        foreach (PluginInventoryItem item in ownedItems)
        {
            string? group = Classify(item);
            if (group is null)
                continue;

            totals[group] = totals.GetValueOrDefault(group) + Math.Max(item.StackSize, 1);
            representative[group] = item; // last one wins, same as "most recently observed"
        }

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach ((string group, int total) in totals)
        {
            PluginInventoryItem sample = representative[group];
            if (!_tracked.TryGetValue(group, out TrackedConsumable? tracked))
            {
                tracked = new TrackedConsumable(group, sample.ObjectClass, minutesToRetain, _timeProvider);
                _tracked[group] = tracked;
            }

            tracked.Icon = sample.IconId;
            tracked.ItemValue = sample.StackSize > 0 ? sample.Value / sample.StackSize : sample.Value;
            tracked.History.AddSnapShot(now, total, minutesToRetain);
        }

        // A tracked name with nothing left in inventory keeps its history (so
        // the depletion math still has something to look at) but its count
        // drops to zero, same as the original re-scanning found nothing.
        foreach ((string group, TrackedConsumable tracked) in _tracked)
        {
            if (!totals.ContainsKey(group))
                tracked.History.AddSnapShot(now, 0, minutesToRetain);
        }

        Changed?.Invoke();
    }

    /// <summary>The tracked item with the smallest positive time-to-depletion, or null.</summary>
    public TrackedConsumable? NextItemToBeDepleted(TimeSpan period)
    {
        TrackedConsumable? best = null;
        TimeSpan bestTime = TimeSpan.MaxValue;

        foreach (TrackedConsumable tracked in _tracked.Values)
        {
            TimeSpan depletion = tracked.History.GetTimeToDepletion(period);
            if (depletion <= TimeSpan.Zero || depletion >= bestTime)
                continue;

            best = tracked;
            bestTime = depletion;
        }

        return best;
    }
}
