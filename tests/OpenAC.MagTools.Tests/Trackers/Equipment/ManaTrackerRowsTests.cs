using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Trackers.Equipment;
using Xunit;

namespace OpenAC.MagTools.Tests.Trackers.Equipment;

public sealed class ManaTrackerRowsTests
{
    private static PluginInventoryItem Item(
        uint objectId, string name, uint validLocations = 1u, uint equippedLocation = 1u,
        int currentMana = 10, int maximumMana = 100)
        => new(
            objectId, 0u, name, 0u, 0u, 0u, validLocations, equippedLocation, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ItemCurrentMana = currentMana,
            ItemMaximumMana = maximumMana,
        };

    private static readonly FakeSpellCatalog Spells = new();

    [Fact]
    public void Aetheria_named_items_are_excluded_from_the_list()
    {
        var tracker = new EquipmentTracker();
        EquipmentTrackedItem tracked = tracker.GetOrAdd(1u);
        tracked.UpdateSnapshot(Item(1u, "Aetheria of the Champion"), default, DateTime.UtcNow);

        var rows = ManaTrackerRows.Build(tracker, Spells, [], TimeProvider.System);
        Assert.Empty(rows);
    }

    [Fact]
    public void Cloaks_are_excluded_from_the_list()
    {
        var tracker = new EquipmentTracker();
        EquipmentTrackedItem tracked = tracker.GetOrAdd(1u);
        tracked.UpdateSnapshot(
            Item(1u, "Cloak of Wisdom", validLocations: EquipmentTracker.CloakValidLocations),
            default, DateTime.UtcNow);

        var rows = ManaTrackerRows.Build(tracker, Spells, [], TimeProvider.System);
        Assert.Empty(rows);
    }

    [Fact]
    public void An_ordinary_item_is_listed_with_the_calculated_over_maximum_mana_text()
    {
        var tracker = new EquipmentTracker();
        EquipmentTrackedItem tracked = tracker.GetOrAdd(1u);
        tracked.UpdateSnapshot(Item(1u, "Ring", currentMana: 30, maximumMana: 100), default, DateTime.UtcNow);
        tracked.OnIdentReceived(EmptyProperties(), DateTime.UtcNow);

        var rows = ManaTrackerRows.Build(tracker, Spells, [], TimeProvider.System);
        ManaTrackerRows.Row row = Assert.Single(rows);
        Assert.Equal("30 / 100", row.ManaText);
    }

    [Fact]
    public void Rows_sort_ascending_by_remaining_time()
    {
        var tracker = new EquipmentTracker();

        // Item A: Active, burns fast (short remaining time).
        var itemA = tracker.GetOrAdd(1u);
        var worldA = new PluginWorldObject(1u, 0u, "Fast Item", PluginObjectClass.Jewelry, 0u, 0u, 0u)
        {
            SpellIds = [99u],
        };
        itemA.UpdateSnapshot(Item(1u, "Fast Item", currentMana: 2, maximumMana: 100), worldA, DateTime.UtcNow);
        itemA.OnIdentReceived(
            PropertiesWithRate(-1d / 4d), DateTime.UtcNow); // SecondsPerBurn small

        // Item B: Active, more mana banked (longer remaining time).
        var itemB = tracker.GetOrAdd(2u);
        var worldB = new PluginWorldObject(2u, 0u, "Slow Item", PluginObjectClass.Jewelry, 0u, 0u, 0u)
        {
            SpellIds = [99u],
        };
        itemB.UpdateSnapshot(Item(2u, "Slow Item", currentMana: 90, maximumMana: 100), worldB, DateTime.UtcNow);
        itemB.OnIdentReceived(PropertiesWithRate(-1d / 4d), DateTime.UtcNow);

        var rows = ManaTrackerRows.Build(tracker, Spells, [], TimeProvider.System);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Fast Item", rows[0].Name);
        Assert.Equal("Slow Item", rows[1].Name);
    }

    private static PluginItemProperties EmptyProperties()
        => new(
            new Dictionary<uint, int>(), new Dictionary<uint, long>(),
            new Dictionary<uint, bool>(), new Dictionary<uint, double>(),
            new Dictionary<uint, string>(), new Dictionary<uint, uint>(),
            new Dictionary<uint, uint>());

    private static PluginItemProperties PropertiesWithRate(double rate)
        => new(
            new Dictionary<uint, int>(), new Dictionary<uint, long>(),
            new Dictionary<uint, bool>(), new Dictionary<uint, double> { [5u] = rate },
            new Dictionary<uint, string>(), new Dictionary<uint, uint>(),
            new Dictionary<uint, uint>());
}
