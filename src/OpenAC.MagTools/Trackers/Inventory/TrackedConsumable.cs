using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Trackers.Inventory;

/// <summary>
/// Port of the original's <c>TrackedInventory : ValueSnapShotGroup</c> — one
/// tracked consumable name (or synthetic group name), its representative
/// display fields, and its 60-minute count history.
/// </summary>
public sealed class TrackedConsumable(string name, PluginObjectClass objectClass, int minutesToRetain, TimeProvider? timeProvider = null)
{
    public string Name { get; } = name;

    public PluginObjectClass ObjectClass { get; } = objectClass;

    /// <summary>The most recently observed matching item's icon.</summary>
    public uint Icon { get; set; }

    /// <summary>Unit value: <c>Value / StackCount</c> of the most recently observed item.</summary>
    public int ItemValue { get; set; }

    public Trackers.ValueSnapShotGroup History { get; } = new(minutesToRetain, timeProvider);

    public int CurrentCount => History.LastKnownValue;
}
