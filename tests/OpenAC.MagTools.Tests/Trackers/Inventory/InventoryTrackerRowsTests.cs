using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Trackers.Inventory;
using Xunit;

namespace OpenAC.MagTools.Tests.Trackers.Inventory;

public sealed class InventoryTrackerRowsTests
{
    private static PluginInventoryItem Item(
        uint objectId, string name, PluginObjectClass objectClass, int stackSize = 1, int value = 0)
        => new(
            objectId, 0u, name, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u,
            stackSize, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ObjectClass = objectClass,
            Value = value,
        };

    [Fact]
    public void Build_lays_out_the_fixed_profit_block_and_header_rows()
    {
        var host = new InventoryTrackerHost(new FakeHost());
        host.Consumables.Resync([]);
        host.ProfitLoss.Resync([]);

        var rows = InventoryTrackerRows.Build(host);

        Assert.True(rows.Count >= 7);
        Assert.Equal("MMD/h ~5m", rows[0].Average5m);
        Assert.Equal("MMD/h ~1h", rows[0].Average1h);
        Assert.Equal("Peas", rows[1].Name);
        Assert.Equal("Comps", rows[2].Name);
        Assert.Equal("Salvage", rows[3].Name);
        Assert.Equal("Net Profit", rows[4].Name);
        Assert.Equal(string.Empty, rows[5].Name);
        Assert.Equal("Count", rows[6].Count);
        Assert.Equal("Avg/h ~5m", rows[6].Average5m);
        Assert.Equal("Avg/h ~1h", rows[6].Average1h);
        Assert.Equal("(Hrs)", rows[6].Hours);
    }

    [Fact]
    public void A_consumable_row_blanks_all_three_rate_cells_when_the_count_is_zero()
    {
        var host = new InventoryTrackerHost(new FakeHost());
        // Present once, then gone — CurrentCount drops to 0.
        host.Consumables.Resync([Item(1u, "Mana Stone", PluginObjectClass.ManaStone, stackSize: 3)]);
        host.Consumables.Resync([]);
        host.ProfitLoss.Resync([]);

        var rows = InventoryTrackerRows.Build(host);
        InventoryTrackerRows.Row row = rows[7];
        Assert.Equal("0", row.Count);
        Assert.Equal(string.Empty, row.Average5m);
        Assert.Equal(string.Empty, row.Average1h);
        Assert.Equal(string.Empty, row.Hours);
    }

    [Fact]
    public void A_profit_row_blanks_a_rate_cell_that_computes_to_exactly_zero()
    {
        var host = new InventoryTrackerHost(new FakeHost());
        host.Consumables.Resync([]);
        host.ProfitLoss.Resync([]); // Peas/Comps/Salvage/NetProfit all read 0/h

        var rows = InventoryTrackerRows.Build(host);
        // Row 1 is "Peas".
        Assert.Equal(string.Empty, rows[1].Average5m);
        Assert.Equal(string.Empty, rows[1].Average1h);
    }

    [Fact]
    public void Consumable_rows_sort_by_class_zindex_then_descending_unit_value()
    {
        var host = new InventoryTrackerHost(new FakeHost());
        host.Consumables.Resync([
            Item(1u, "Iron Salvage", PluginObjectClass.Salvage, value: 10),
            Item(2u, "Minor Healing Kit", PluginObjectClass.HealingKit, value: 50),
            Item(3u, "Cheap Taper", PluginObjectClass.SpellComponent, value: 5),
            Item(4u, "Expensive Scarab", PluginObjectClass.SpellComponent, value: 900),
        ]);
        host.ProfitLoss.Resync([]);

        var rows = InventoryTrackerRows.Build(host);
        var consumableNames = new List<string>();
        for (int i = 7; i < rows.Count; i++)
            consumableNames.Add(rows[i].Name);

        // SpellComponent (z=0) before HealingKit (z=2) before Salvage (z=4);
        // within SpellComponent, higher unit value first.
        Assert.Equal(
            ["Expensive Scarab", "Cheap Taper", "Minor Healing Kit", "Salvage"],
            consumableNames);
    }
}
