using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Trackers.Equipment;
using OpenAC.MagTools.Trackers.Inventory;
using OpenAC.MagTools.Ui;
using Xunit;

namespace OpenAC.MagTools.Tests.Ui;

public sealed class ManaAndInventoryItemsPageViewModelTests
{
    [Fact]
    public void ManaPageViewModel_row_getters_do_not_reallocate_between_polls_when_nothing_changed()
    {
        var host = new FakeHost();
        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Ring", equippedLocation: 1u));
        var equipmentHost = new EquipmentTrackerHost(host);
        equipmentHost.Start();

        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var vm = new ManaPageViewModel(settings, equipmentHost, host);

        IReadOnlyList<uint> first = vm.ItemIcons;
        IReadOnlyList<uint> second = vm.ItemIcons;

        Assert.Same(first, second);
    }

    [Fact]
    public void ManaPageViewModel_row_getters_rebuild_only_after_Changed_fires()
    {
        var host = new FakeHost();
        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Ring", equippedLocation: 1u));
        var equipmentHost = new EquipmentTrackerHost(host);
        equipmentHost.Start();

        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var vm = new ManaPageViewModel(settings, equipmentHost, host);

        IReadOnlyList<string> before = vm.ItemNames;
        equipmentHost.Tracker.RaiseChanged();
        IReadOnlyList<string> after = vm.ItemNames;

        Assert.NotSame(before, after);
    }

    [Fact]
    public void InventoryItemsPageViewModel_row_getters_do_not_reallocate_between_polls_when_nothing_changed()
    {
        var host = new FakeHost();
        var inventoryHost = new InventoryTrackerHost(host);
        inventoryHost.Consumables.Resync([]);
        inventoryHost.ProfitLoss.Resync([]);

        var vm = new InventoryItemsPageViewModel(inventoryHost);

        IReadOnlyList<uint> first = vm.Icons;
        IReadOnlyList<uint> second = vm.Icons;

        Assert.Same(first, second);
    }
}
