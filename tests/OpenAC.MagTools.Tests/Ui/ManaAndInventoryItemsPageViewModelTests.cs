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
    public void The_mana_time_cell_changes_after_the_clock_advances_with_no_events()
    {
        // MEDIUM-3: with the fix, EquipmentTrackerHost's idle coalescing tick
        // re-raises Tracker.Changed at 2 Hz even when nothing else happened,
        // so the bound Mana list's countdown cell keeps advancing purely
        // from the clock instead of freezing until the next real event.
        var host = new FakeHost();
        var clock = new OpenAC.MagTools.Tests.Trackers.Inventory.FakeTimeProvider();
        var world = new PluginWorldObject(1u, 0u, "Ring", PluginObjectClass.Jewelry, 0u, 0u, 0u)
        {
            SpellIds = [99u],
            ActiveSpellIds = [99u],
        };
        host.Automation.Objects.Objects.Add(world);
        host.Automation.Items.Owned.Add(
            new PluginInventoryItem(
                1u, 0u, "Ring", 0u, 0u, 0u, 0u, 1u, 0u, 0u, 0u,
                1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
            {
                ItemCurrentMana = 50,
                ItemMaximumMana = 100,
            });
        host.Automation.Items.Properties[1u] = new PluginItemProperties(
            new Dictionary<uint, int>(), new Dictionary<uint, long>(),
            new Dictionary<uint, bool>(), new Dictionary<uint, double> { [5u] = -1d / 18d },
            new Dictionary<uint, string>(), new Dictionary<uint, uint>(),
            new Dictionary<uint, uint>());

        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        var equipmentHost = new EquipmentTrackerHost(host, clock);
        equipmentHost.Start(scheduler); // resyncs immediately, tracks item 1u
        host.Events.RaiseObjectChanged(1u, PluginObjectChangeKind.IdentReceived); // Active, burn rate set
        scheduler.Tick(0.5); // settles the dirty flag IdentReceived set, so the next tick is genuinely idle

        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var vm = new ManaPageViewModel(settings, equipmentHost, host, clock);

        string before = Assert.Single(vm.ItemTime);

        // No ObjectChanged at all from here on — only the clock moves and
        // the coalescing tick's IDLE branch fires (MEDIUM-3). 15 minutes at
        // a 20 s-per-point burn rate is enough to cross a whole displayed
        // minute boundary (50 -> 5 mana remaining), not just round to the
        // same "0h16m" text.
        clock.Advance(TimeSpan.FromMinutes(15));
        scheduler.Tick(0.5);

        string after = Assert.Single(vm.ItemTime);
        Assert.NotEqual(before, after);
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
    public void GetState_is_allocation_bounded_with_no_per_call_collection()
    {
        var tracker = new EquipmentTracker();
        var item = tracker.GetOrAdd(1u);
        // ActiveSpellIds deliberately does NOT satisfy spell 99 — GetState
        // must fall through to the (now per-call-collection-free, M6) player-
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
