using System.Linq;
using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Trackers.Inventory;
using Xunit;

namespace OpenAC.MagTools.Tests.Trackers.Inventory;

public sealed class ConsumablesTrackerTests
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

    [Theory]
    [InlineData("Prismatic Taper", PluginObjectClass.SpellComponent, "Prismatic Taper")]
    [InlineData("Green Scarab", PluginObjectClass.SpellComponent, "Green Scarab")]
    [InlineData("Mana Stone", PluginObjectClass.ManaStone, "Mana Stone")]
    [InlineData("Minor Healing Kit", PluginObjectClass.HealingKit, "Minor Healing Kit")]
    [InlineData("Trade Note (100pp)", PluginObjectClass.TradeNote, "Trade Note")]
    [InlineData("Iron Salvage (5)", PluginObjectClass.Salvage, "Salvage")]
    [InlineData("Corrupted Essence", PluginObjectClass.Misc, "Corrupted Essence")]
    [InlineData("Lesser Corrupted Essence", PluginObjectClass.Misc, "Corrupted Essence")]
    public void Classify_matches_the_original_rules(string name, PluginObjectClass objectClass, string expectedGroup)
    {
        PluginInventoryItem item = Item(1u, name, objectClass);
        Assert.Equal(expectedGroup, ConsumablesTracker.Classify(item));
    }

    [Theory]
    [InlineData("Iron Sword", PluginObjectClass.MeleeWeapon)]
    [InlineData("A Component with no matching word", PluginObjectClass.SpellComponent)]
    [InlineData("Loot", PluginObjectClass.Misc)]
    public void Classify_returns_null_for_untracked_items(string name, PluginObjectClass objectClass)
    {
        PluginInventoryItem item = Item(1u, name, objectClass);
        Assert.Null(ConsumablesTracker.Classify(item));
    }

    [Fact]
    public void Resync_sums_stack_sizes_across_matching_items_into_one_group()
    {
        var clock = new FakeTimeProvider();
        var tracker = new ConsumablesTracker(timeProvider: clock);

        tracker.Resync([
            Item(1u, "Trade Note (100pp)", PluginObjectClass.TradeNote, stackSize: 1, value: 100),
            Item(2u, "Trade Note (250pp)", PluginObjectClass.TradeNote, stackSize: 1, value: 250),
        ]);

        TrackedConsumable tradeNotes = Assert.Single(tracker.Tracked);
        Assert.Equal("Trade Note", tradeNotes.Name);
        Assert.Equal(2, tradeNotes.CurrentCount);
    }

    [Fact]
    public void Resync_drops_a_groups_count_to_zero_once_nothing_matches_anymore()
    {
        var clock = new FakeTimeProvider();
        var tracker = new ConsumablesTracker(timeProvider: clock);

        tracker.Resync([Item(1u, "Mana Stone", PluginObjectClass.ManaStone, stackSize: 3)]);
        Assert.Equal(3, tracker.Tracked.First().CurrentCount);

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Resync([]);
        Assert.Equal(0, tracker.Tracked.First().CurrentCount);
    }

    [Fact]
    public void NextItemToBeDepleted_picks_the_soonest_positive_depletion()
    {
        var clock = new FakeTimeProvider();
        var tracker = new ConsumablesTracker(timeProvider: clock);

        tracker.Resync([Item(1u, "Mana Stone", PluginObjectClass.ManaStone, stackSize: 100)]);
        clock.Advance(TimeSpan.FromMinutes(10));
        tracker.Resync([Item(1u, "Mana Stone", PluginObjectClass.ManaStone, stackSize: 50)]);

        TrackedConsumable? next = tracker.NextItemToBeDepleted(TimeSpan.FromMinutes(10));
        Assert.NotNull(next);
        Assert.Equal("Mana Stone", next!.Name);
    }

    [Fact]
    public void Clear_removes_every_tracked_name()
    {
        var tracker = new ConsumablesTracker();
        tracker.Resync([Item(1u, "Mana Stone", PluginObjectClass.ManaStone)]);
        Assert.NotEmpty(tracker.Tracked);

        tracker.Clear();
        Assert.Empty(tracker.Tracked);
    }
}

/// <summary>A settable clock shared by the P5 inventory-tracker tests.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan span) => Now += span;
}
