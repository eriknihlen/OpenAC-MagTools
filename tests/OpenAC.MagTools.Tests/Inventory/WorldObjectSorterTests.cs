using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Inventory;
using OpenAC.MagTools.Tests.Fakes;

namespace OpenAC.MagTools.Tests.Inventory;

public sealed class WorldObjectSorterTests
{
    private const uint MyId = 1u;

    private static PluginInventoryItem Item(
        uint id,
        uint containerId,
        uint equippedLocation = 0u,
        int containerSlot = -1,
        PluginObjectClass objectClass = PluginObjectClass.Misc)
        => FakeItems.Item(id, "Item" + id, equippedLocation) with
        {
            ContainerObjectId = containerId,
            ContainerSlot = containerSlot,
            ObjectClass = objectClass,
        };

    [Fact]
    public void MainPackItemsSortBeforeSidePackItems()
    {
        var mainPackItem = Item(1u, MyId);
        var sidePackItem = Item(2u, 50u);
        var items = new List<PluginInventoryItem> { sidePackItem, mainPackItem };

        items.Sort(new WorldObjectSorter(MyId, items));

        Assert.Equal(1u, items[0].ObjectId);
    }

    [Fact]
    public void WithinMainPackEquippedSortsBeforeUnequipped()
    {
        var equipped = Item(1u, MyId, equippedLocation: 1u);
        var unequipped = Item(2u, MyId);
        var items = new List<PluginInventoryItem> { unequipped, equipped };

        items.Sort(new WorldObjectSorter(MyId, items));

        Assert.Equal(1u, items[0].ObjectId);
    }

    [Fact]
    public void WithinMainPackNonContainersSortBeforeContainersAndFoci()
    {
        var container = Item(1u, MyId, objectClass: PluginObjectClass.Container);
        var ordinary = Item(2u, MyId, objectClass: PluginObjectClass.Misc);
        var items = new List<PluginInventoryItem> { container, ordinary };

        items.Sort(new WorldObjectSorter(MyId, items));

        Assert.Equal(2u, items[0].ObjectId);
    }

    [Fact]
    public void WithinMainPackUnequippedNonContainersSortBySlot()
    {
        var slot2 = Item(1u, MyId, containerSlot: 2);
        var slot0 = Item(2u, MyId, containerSlot: 0);
        var items = new List<PluginInventoryItem> { slot2, slot0 };

        items.Sort(new WorldObjectSorter(MyId, items));

        Assert.Equal(2u, items[0].ObjectId); // slot 0 first
    }

    [Fact]
    public void SidePackItemsSortByContainerThenSlot()
    {
        var containerA = Item(1u, 50u, containerSlot: 3);
        var containerB = Item(2u, 40u, containerSlot: 0);
        var items = new List<PluginInventoryItem> { containerA, containerB };

        items.Sort(new WorldObjectSorter(MyId, items));

        // Lower container id first (40 < 50).
        Assert.Equal(2u, items[0].ObjectId);
    }

    [Fact]
    public void EquippedItemsWithHostShapedFieldsSortBeforeMainPackAndByEquippedLocation()
    {
        // MEDIUM-4: on this host every equipped item has ContainerObjectId
        // == 0 (or whatever container it was equipped FROM) and
        // ContainerSlot == -1 -- the wielding entity's id lives in
        // WielderObjectId instead (same host-shape fact as defect 9's
        // IsEquippedByMe fix). The old "directly mine" test
        // (ContainerObjectId == myObjectId alone) made these items sort as
        // if they were in an unrelated side pack, after every main-pack
        // item, and the equipped-ordering block was unreachable.
        var equippedSecond = Item(2u, containerId: 0u, equippedLocation: 5u) with { WielderObjectId = MyId };
        var equippedFirst = Item(1u, containerId: 0u, equippedLocation: 2u) with { WielderObjectId = MyId };
        var mainPackItem = Item(3u, MyId);
        var items = new List<PluginInventoryItem> { mainPackItem, equippedSecond, equippedFirst };

        items.Sort(new WorldObjectSorter(MyId, items));

        Assert.Equal(1u, items[0].ObjectId); // lower EquippedLocation first
        Assert.Equal(2u, items[1].ObjectId);
        Assert.Equal(3u, items[2].ObjectId); // main pack after equipped
    }

    [Fact]
    public void NeitherOwnedItemFallsBackToObjectId()
    {
        var high = new PluginInventoryItem(200u, 0u, "B", 0u, 999u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0);
        var low = new PluginInventoryItem(100u, 0u, "A", 0u, 999u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0);

        // Neither is in the (empty) owned set, so both fall to "neither in inventory".
        var sorter = new WorldObjectSorter(MyId, []);
        var items = new List<PluginInventoryItem> { high, low };
        items.Sort(sorter);

        Assert.Equal(100u, items[0].ObjectId);
    }
}
