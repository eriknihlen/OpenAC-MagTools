using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Trackers.Equipment;

/// <summary>
/// Port of the original's equipment ("mana") tracker aggregate: the set of
/// items currently equipped by the local player, keyed by object id, plus the
/// HUD/GUI aggregates the original exposed off the whole set.
/// </summary>
/// <remarks>
/// Ammo's raw property number (8388608) is <c>EquippedLocation</c> here,
/// matching the original's "ammo" test against Decal's <c>EquippedSlots</c> —
/// same numeric convention, different accessor name.
/// </remarks>
public sealed class EquipmentTracker(TimeProvider? timeProvider = null)
{
    /// <summary>Decal's <c>EquipableSlots</c> value for a cloak slot.</summary>
    public const uint CloakValidLocations = 134217728u;

    /// <summary>Decal's <c>EquippedSlots</c> value for the ammo slot.</summary>
    public const uint AmmoEquippedLocation = 8388608u;

    private readonly Dictionary<uint, EquipmentTrackedItem> _items = [];
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>Raised whenever the tracked set or any tracked item's data changes.</summary>
    public event Action? Changed;

    public IReadOnlyCollection<EquipmentTrackedItem> Items => _items.Values;

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public void Clear()
    {
        if (_items.Count == 0)
            return;
        _items.Clear();
        Changed?.Invoke();
    }

    public EquipmentTrackedItem GetOrAdd(uint objectId)
    {
        if (_items.TryGetValue(objectId, out EquipmentTrackedItem? existing))
            return existing;

        var created = new EquipmentTrackedItem(objectId);
        _items[objectId] = created;
        return created;
    }

    public void Remove(uint objectId)
    {
        if (_items.Remove(objectId))
            Changed?.Invoke();
    }

    public void RaiseChanged() => Changed?.Invoke();

    /// <summary>
    /// Excluded from the Mana list's display (still tracked and still counted
    /// in the aggregates below) — names containing "Aetheria", and cloaks.
    /// </summary>
    public static bool IsHiddenFromList(EquipmentTrackedItem item)
        => item.Item.Name.Contains("Aetheria", StringComparison.Ordinal)
            || item.Item.ValidLocations == CloakValidLocations;

    public static bool IsAmmo(EquipmentTrackedItem item)
        => item.Item.EquippedLocation == AmmoEquippedLocation;

    /// <summary>
    /// The minimum positive <c>ManaTimeRemaining</c> across every tracked
    /// item, or <see cref="TimeSpan.MaxValue"/> when none will empty.
    /// </summary>
    public TimeSpan RemainingTimeBeforeNextEmptyItem(ISpellCatalog spells, IReadOnlyList<PluginActiveEnchantment> playerEnchantments)
    {
        DateTime now = UtcNow;
        TimeSpan best = TimeSpan.MaxValue;
        foreach (EquipmentTrackedItem item in _items.Values)
        {
            EquipmentTrackedItemState state = item.GetState(spells, playerEnchantments);
            TimeSpan remaining = item.ManaTimeRemaining(now, state);
            if (remaining > TimeSpan.Zero && remaining < best)
                best = remaining;
        }

        return best;
    }

    public int ManaNeededToRefillItems(ISpellCatalog spells, IReadOnlyList<PluginActiveEnchantment> playerEnchantments)
    {
        DateTime now = UtcNow;
        int total = 0;
        foreach (EquipmentTrackedItem item in _items.Values)
        {
            EquipmentTrackedItemState state = item.GetState(spells, playerEnchantments);
            total += item.ManaNeededToRefill(now, state);
        }

        return total;
    }

    public int NumberOfInactiveItems(ISpellCatalog spells, IReadOnlyList<PluginActiveEnchantment> playerEnchantments)
    {
        int count = 0;
        foreach (EquipmentTrackedItem item in _items.Values)
        {
            if (item.GetState(spells, playerEnchantments) == EquipmentTrackedItemState.NotActive)
                count++;
        }

        return count;
    }

    /// <summary>Items with id data and <c>Retained == false</c>, excluding ammo.</summary>
    public int NumberOfUnretainedItems()
    {
        int count = 0;
        foreach (EquipmentTrackedItem item in _items.Values)
        {
            if (item.HasIdData && item.Retained == false && !IsAmmo(item))
                count++;
        }

        return count;
    }
}
