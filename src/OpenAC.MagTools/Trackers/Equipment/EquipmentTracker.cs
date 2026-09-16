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

    /// <summary>
    /// ACE <c>PublicWeenieFlags.Retained</c> (0x01000000). The host does not
    /// project the appraisal-derived Retained bool this tracker used to read
    /// (see <see cref="NumberOfUnretainedItems"/>'s remarks); this bit on the
    /// owned-item snapshot's <c>PublicFlags</c> is the equivalent signal and
    /// is present without waiting for a full ident.
    /// </summary>
    public const uint RetainedPublicFlag = 0x01000000u;

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
    /// in the aggregates below) — names containing "Aetheria", cloaks, and
    /// archer/missile ammo (M1: the original's <c>ManaTrackerGUI</c> checks
    /// all three; this port previously only ported the first two).
    /// </summary>
    public static bool IsHiddenFromList(EquipmentTrackedItem item)
        => (!string.IsNullOrEmpty(item.Item.Name)
                && item.Item.Name.Contains("Aetheria", StringComparison.Ordinal))
            || item.Item.ValidLocations == CloakValidLocations
            || IsAmmo(item);

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

    /// <summary>
    /// Items with id data and the <see cref="RetainedPublicFlag"/> bit unset
    /// on their owned-item snapshot, excluding ammo.
    /// </summary>
    /// <remarks>
    /// H2: this used to gate on <c>item.Retained == false</c>, the
    /// appraisal-derived bool from <see cref="ItemInfo.ItemModel.RetainedKey"/>
    /// — but that bool is only populated when the host's appraisal payload
    /// happens to carry that property id, so an unretained item whose
    /// appraisal omits it (the common case) was silently never counted, no
    /// matter how many unretained items were equipped. The original reads
    /// Decal's <c>wo.Values(BoolValueKey.Retained)</c>, which defaults to
    /// <c>false</c> when absent — the same "absent means unretained" default
    /// the owned-item snapshot's <see cref="PluginInventoryItem.PublicFlags"/>
    /// bit test gives for free, with no ident wait. See docs/deviations.md.
    /// </remarks>
    public int NumberOfUnretainedItems()
    {
        int count = 0;
        foreach (EquipmentTrackedItem item in _items.Values)
        {
            if (item.HasIdData
                && (item.Item.PublicFlags & RetainedPublicFlag) == 0
                && !IsAmmo(item))
            {
                count++;
            }
        }

        return count;
    }
}
