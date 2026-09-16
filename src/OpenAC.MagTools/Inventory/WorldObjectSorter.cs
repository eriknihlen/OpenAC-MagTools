using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Inventory;

/// <summary>
/// Ports <c>InventoryExporter.WorldObjectSorter</c>, verbatim order: items in
/// the main pack first; within the main pack, equipped before unequipped
/// (equipped ordered by equip-slot bitmask), then non-container/non-foci
/// before containers/foci, then by container slot; then items in side packs
/// ordered by container id then slot; then by object id.
/// </summary>
/// <remarks>
/// The original ordered equipped items by <c>EquippedSlots</c> — a count of
/// how many equip slots the item occupies (a two-handed weapon or a full
/// hauberk occupies more than one). The host's <see cref="PluginInventoryItem"/>
/// exposes only <see cref="PluginInventoryItem.EquippedLocation"/>, the equip
/// slot bitmask itself, not a occupied-slot count. This port orders by that
/// bitmask's numeric value instead — a stable, deterministic ordering that
/// matches the original whenever equip-slot counts happen to correlate with
/// the bitmask's magnitude, but is not guaranteed identical in every case.
/// See <c>docs/deviations.md</c>.
/// </remarks>
public sealed class WorldObjectSorter : IComparer<PluginInventoryItem>
{
    private readonly uint _myObjectId;
    private readonly HashSet<uint> _ownedObjectIds;

    public WorldObjectSorter(uint myObjectId, IReadOnlyList<PluginInventoryItem> ownedItems)
    {
        ArgumentNullException.ThrowIfNull(ownedItems);
        _myObjectId = myObjectId;
        _ownedObjectIds = [.. ownedItems.Select(static item => item.ObjectId)];
    }

    public int Compare(PluginInventoryItem x, PluginInventoryItem y)
    {
        if (x.ContainerObjectId == _myObjectId && x.ContainerObjectId != y.ContainerObjectId)
            return -1;
        if (y.ContainerObjectId == _myObjectId && x.ContainerObjectId != y.ContainerObjectId)
            return 1;

        if (x.ContainerObjectId == _myObjectId && x.ContainerObjectId == y.ContainerObjectId)
        {
            if (x.IsEquipped && !y.IsEquipped)
                return -1;
            if (!x.IsEquipped && y.IsEquipped)
                return 1;

            if (x.IsEquipped && y.IsEquipped)
                return x.EquippedLocation.CompareTo(y.EquippedLocation);

            bool xIsContainerLike = x.ObjectClass is PluginObjectClass.Container or PluginObjectClass.Foci;
            bool yIsContainerLike = y.ObjectClass is PluginObjectClass.Container or PluginObjectClass.Foci;

            if (!xIsContainerLike && yIsContainerLike)
                return -1;
            if (xIsContainerLike && !yIsContainerLike)
                return 1;

            return x.ContainerSlot.CompareTo(y.ContainerSlot);
        }

        bool xInInventory = _ownedObjectIds.Contains(x.ObjectId);
        bool yInInventory = _ownedObjectIds.Contains(y.ObjectId);

        if (xInInventory && !yInInventory)
            return -1;
        if (!xInInventory && yInInventory)
            return 1;

        if (xInInventory && yInInventory)
        {
            if (x.ContainerObjectId != y.ContainerObjectId)
                return x.ContainerObjectId.CompareTo(y.ContainerObjectId);
            return x.ContainerSlot.CompareTo(y.ContainerSlot);
        }

        return x.ObjectId.CompareTo(y.ObjectId);
    }
}
