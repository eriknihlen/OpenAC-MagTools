using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Trackers.ProfitLoss;
using Xunit;

namespace OpenAC.MagTools.Tests.Trackers.ProfitLoss;

public sealed class ProfitLossTrackerTests
{
    private static PluginInventoryItem Item(uint objectId, string name, PluginObjectClass objectClass, int value)
        => new(
            objectId, 0u, name, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ObjectClass = objectClass,
            Value = value,
        };

    [Fact]
    public void Resync_splits_spell_components_into_peas_and_comps()
    {
        var tracker = new ProfitLossTracker();
        tracker.Resync([
            Item(1u, "Prismatic Pea", PluginObjectClass.SpellComponent, 500),
            Item(2u, "Scarab of Fire", PluginObjectClass.SpellComponent, 300),
        ]);

        Assert.Equal(500, tracker.Peas.LastKnownValue);
        Assert.Equal(300, tracker.Comps.LastKnownValue);
    }

    [Fact]
    public void NetProfit_sums_components_salvage_trade_notes_and_money()
    {
        var tracker = new ProfitLossTracker();
        tracker.Resync([
            Item(1u, "Prismatic Pea", PluginObjectClass.SpellComponent, 500),
            Item(2u, "Iron Salvage", PluginObjectClass.Salvage, 200),
            Item(3u, "Trade Note", PluginObjectClass.TradeNote, 1000),
            Item(4u, "Pyreals", PluginObjectClass.Money, 50),
            Item(5u, "Sword", PluginObjectClass.MeleeWeapon, 999), // not part of any series
        ]);

        Assert.Equal(500 + 200 + 1000 + 50, tracker.NetProfit.LastKnownValue);
        Assert.Equal(200, tracker.Salvage.LastKnownValue);
    }

    [Fact]
    public void MmdPerHour_divides_the_rate_by_250000()
    {
        var clock = new FakeTimeProvider();
        var tracker = new ProfitLossTracker(clock);

        tracker.Resync([]);
        clock.Advance(TimeSpan.FromHours(1));
        tracker.Resync([Item(1u, "Trade Note", PluginObjectClass.TradeNote, 250000)]);

        double perHour = ProfitLossTracker.MmdPerHour(tracker.NetProfit, TimeSpan.FromHours(1));
        Assert.Equal(1d, perHour, 3);
    }

    [Fact]
    public void Resync_raises_Changed()
    {
        var tracker = new ProfitLossTracker();
        bool raised = false;
        tracker.Changed += () => raised = true;

        tracker.Resync([]);

        Assert.True(raised);
    }
}

/// <summary>A settable clock for exact MMD/h assertions.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan span) => Now += span;
}
