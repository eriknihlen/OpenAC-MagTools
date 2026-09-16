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
    public void ManaPageViewModel_TotalText_does_not_reallocate_between_polls_when_nothing_changed()
    {
        var host = new FakeHost();
        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Ring", equippedLocation: 1u));
        var equipmentHost = new EquipmentTrackerHost(host);
        equipmentHost.Start();

        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var vm = new ManaPageViewModel(settings, equipmentHost, host);

        // M6: TotalText/UnretainedTotalText used to format a fresh string on
        // EVERY poll regardless of whether the tracker changed — now they
        // are computed once per RefreshIfDirty pass, same as the row arrays.
        string first = vm.TotalText;
        string second = vm.TotalText;
        Assert.Same(first, second);

        string firstUnretained = vm.UnretainedTotalText;
        string secondUnretained = vm.UnretainedTotalText;
        Assert.Same(firstUnretained, secondUnretained);
    }

    [Fact]
    public void ManaPageViewModel_TotalText_rebuilds_only_after_Changed_fires()
    {
        var host = new FakeHost();
        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Ring", equippedLocation: 1u));
        var equipmentHost = new EquipmentTrackerHost(host);
        equipmentHost.Start();

        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var vm = new ManaPageViewModel(settings, equipmentHost, host);

        string before = vm.TotalText;
        equipmentHost.Tracker.RaiseChanged();
        string after = vm.TotalText;

        // Same VALUE (nothing about the tracker actually changed), but a
        // freshly-formatted instance — proves the cache really did rebuild.
        Assert.Equal(before, after);
        Assert.NotSame(before, after);
    }

    [Fact]
    public void GetState_does_not_allocate_per_call()
    {
        var tracker = new EquipmentTracker();
        var item = tracker.GetOrAdd(1u);
        // ActiveSpellIds deliberately does NOT satisfy spell 99 — GetState
        // must fall through to the (now allocation-free, M6) player-
        // enchantment check below to resolve Active vs NotActive.
        var world = new PluginWorldObject(1u, 0u, "Ring", PluginObjectClass.Jewelry, 0u, 0u, 0u)
        {
            SpellIds = [99u],
            ActiveSpellIds = [],
        };
        item.UpdateSnapshot(
            new PluginInventoryItem(
                1u, 0u, "Ring", 0u, 0u, 0u, 0u, 1u, 0u, 0u, 0u,
                1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
            {
                ItemCurrentMana = 30,
                ItemMaximumMana = 100,
            },
            world,
            DateTime.UtcNow);
        item.OnIdentReceived(
            new PluginItemProperties(
                new Dictionary<uint, int>(), new Dictionary<uint, long>(),
                new Dictionary<uint, bool>(), new Dictionary<uint, double>(),
                new Dictionary<uint, string>(), new Dictionary<uint, uint>(),
                new Dictionary<uint, uint>()),
            DateTime.UtcNow);

        var spells = new OpenAC.MagTools.Tests.Trackers.Equipment.FakeSpellCatalog();
        spells.Register(99u, "Test Spell", family: 1, difficulty: 1);
        spells.Register(500u, "Item-Cast Enchantment", family: 1, difficulty: 1);
        IReadOnlyList<PluginActiveEnchantment> enchantments = [new PluginActiveEnchantment(500u, 1u, 1, 0d)];

        // Warm up (JIT, any lazy static init) before measuring.
        item.GetState(spells, enchantments);

        const int iterations = 1000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            item.GetState(spells, enchantments);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // M6 removed the per-call `List<PluginActiveEnchantment>` this method
        // used to build (and grow via repeated Add) on every single call —
        // that alone was tens of bytes per call, scaling with the number of
        // player enchantments. A generous per-call ceiling (rather than a
        // strict zero) tolerates whatever the runtime's interface-typed
        // foreach enumerator dispatch costs without masking a reintroduced
        // per-call collection.
        double perCall = (double)allocated / iterations;
        Assert.True(perCall < 128d, $"GetState allocated {perCall:F1} bytes/call on average.");
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
