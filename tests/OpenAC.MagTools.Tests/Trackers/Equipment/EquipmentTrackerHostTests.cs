using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Trackers.Equipment;
using Xunit;

namespace OpenAC.MagTools.Tests.Trackers.Equipment;

public sealed class EquipmentTrackerHostTests
{
    private static PluginInventoryItem EquippedItem(uint objectId, string name = "Ring")
        => new(
            objectId, 0u, name, 0u, 0u, 0u, 0u, 1u /* equipped */, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ItemCurrentMana = 50,
            ItemMaximumMana = 100,
        };

    [Fact]
    public void Start_tracks_every_currently_equipped_item_and_requests_its_id()
    {
        var host = new FakeHost();
        host.Automation.Items.Owned.Add(EquippedItem(10u));
        host.Automation.Items.Owned.Add(EquippedItem(11u, "Cloak"));

        var equipmentHost = new EquipmentTrackerHost(host);
        equipmentHost.Start();

        Assert.Equal(2, equipmentHost.Tracker.Items.Count);
        Assert.Contains(10u, host.Automation.Objects.IdentifyRequests);
        Assert.Contains(11u, host.Automation.Objects.IdentifyRequests);
    }

    [Fact]
    public void Start_ignores_unequipped_owned_items()
    {
        var host = new FakeHost();
        var unequipped = FakeItems.Item(20u, "Loose Gem", equippedLocation: 0u);
        host.Automation.Items.Owned.Add(unequipped);

        var equipmentHost = new EquipmentTrackerHost(host);
        equipmentHost.Start();

        Assert.Empty(equipmentHost.Tracker.Items);
    }

    [Fact]
    public void An_unequip_ObjectChanged_removes_the_item_from_the_tracked_set()
    {
        var host = new FakeHost();
        var item = EquippedItem(10u);
        host.Automation.Items.Owned.Add(item);

        var equipmentHost = new EquipmentTrackerHost(host);
        equipmentHost.Start();
        Assert.Single(equipmentHost.Tracker.Items);

        // Simulate an unequip: the same item is still owned but no longer equipped.
        host.Automation.Items.Owned.Clear();
        host.Automation.Items.Owned.Add(item with { EquippedLocation = 0u });
        host.Events.RaiseObjectChanged(10u, PluginObjectChangeKind.Updated);

        Assert.Empty(equipmentHost.Tracker.Items);
    }

    [Fact]
    public void IdentReceived_captures_ManaRateOfChange_and_Retained()
    {
        var host = new FakeHost();
        host.Automation.Items.Owned.Add(EquippedItem(10u));
        host.Automation.Items.Properties[10u] = new PluginItemProperties(
            new Dictionary<uint, int>(),
            new Dictionary<uint, long>(),
            new Dictionary<uint, bool> { [91u] = false },
            new Dictionary<uint, double> { [5u] = -1d / 18d },
            new Dictionary<uint, string>(),
            new Dictionary<uint, uint>(),
            new Dictionary<uint, uint>());

        var equipmentHost = new EquipmentTrackerHost(host);
        equipmentHost.Start();
        host.Events.RaiseObjectChanged(10u, PluginObjectChangeKind.IdentReceived);

        EquipmentTrackedItem tracked = Assert.Single(equipmentHost.Tracker.Items);
        Assert.True(tracked.HasIdData);
        Assert.Equal(-1d / 18d, tracked.ManaRateOfChange);
        Assert.False(tracked.Retained);
        Assert.Equal(1, equipmentHost.Tracker.NumberOfUnretainedItems());
    }

    [Fact]
    public void Stop_clears_the_tracked_set_and_unsubscribes()
    {
        var host = new FakeHost();
        host.Automation.Items.Owned.Add(EquippedItem(10u));

        var equipmentHost = new EquipmentTrackerHost(host);
        equipmentHost.Start();
        equipmentHost.Stop();

        Assert.Empty(equipmentHost.Tracker.Items);

        // A further ObjectChanged after Stop() must not resurrect anything.
        host.Events.RaiseObjectChanged(10u, PluginObjectChangeKind.Updated);
        Assert.Empty(equipmentHost.Tracker.Items);
    }
}
