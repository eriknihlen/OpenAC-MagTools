using System.Linq;
using AcDream.Plugin.Abstractions;
using OpenAC.MagTools;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Trackers.Inventory;
using Xunit;

namespace OpenAC.MagTools.Tests.Trackers.Inventory;

public sealed class InventoryTrackerHostTests
{
    private static PluginInventoryItem ManaStone(uint objectId, int stackSize = 1)
        => new(
            objectId, 0u, "Mana Stone", 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u,
            stackSize, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.ManaStone,
        };

    [Fact]
    public void Start_primes_both_trackers_immediately()
    {
        var host = new FakeHost();
        host.Automation.Items.Owned.Add(ManaStone(1u, stackSize: 3));

        var inventoryHost = new InventoryTrackerHost(host);
        inventoryHost.Start(new TickScheduler(host.Events, new ChatOutput(host)));

        Assert.Single(inventoryHost.Consumables.Tracked);
        Assert.Equal(3, inventoryHost.Consumables.Tracked.First().CurrentCount);
    }

    [Fact]
    public void An_ObjectChanged_event_resyncs_both_trackers()
    {
        var host = new FakeHost();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        var inventoryHost = new InventoryTrackerHost(host);
        inventoryHost.Start(scheduler);

        host.Automation.Items.Owned.Add(ManaStone(1u, stackSize: 5));
        host.Events.RaiseObjectChanged(1u, PluginObjectChangeKind.Created);

        Assert.Equal(5, inventoryHost.Consumables.Tracked.First().CurrentCount);
    }

    [Fact]
    public void The_500ms_timer_resyncs_without_a_matching_event()
    {
        var host = new FakeHost();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        var inventoryHost = new InventoryTrackerHost(host);
        inventoryHost.Start(scheduler);

        host.Automation.Items.Owned.Add(ManaStone(1u, stackSize: 7));
        scheduler.Tick(0.5); // the 500ms resync tick

        Assert.Equal(7, inventoryHost.Consumables.Tracked.First().CurrentCount);
    }

    [Fact]
    public void Stop_clears_the_consumables_tracker_and_stops_resyncing()
    {
        var host = new FakeHost();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        var inventoryHost = new InventoryTrackerHost(host);
        inventoryHost.Start(scheduler);
        host.Automation.Items.Owned.Add(ManaStone(1u));
        host.Events.RaiseObjectChanged(1u, PluginObjectChangeKind.Created);
        Assert.NotEmpty(inventoryHost.Consumables.Tracked);

        inventoryHost.Stop();
        Assert.Empty(inventoryHost.Consumables.Tracked);

        host.Automation.Items.Owned.Add(ManaStone(2u));
        host.Events.RaiseObjectChanged(2u, PluginObjectChangeKind.Created);
        Assert.Empty(inventoryHost.Consumables.Tracked);
    }
}
