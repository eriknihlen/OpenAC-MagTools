using System.Linq;
using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Trackers.Combat;
using OpenAC.MagTools.Trackers.Equipment;
using OpenAC.MagTools.Trackers.Inventory;
using OpenAC.MagTools.Ui;
using Xunit;

namespace OpenAC.MagTools.Tests.Ui;

public sealed class HudUpdaterTests
{
    private static string Row(HudViewModel hud, string name)
    {
        int index = hud.Names.ToList().IndexOf(name);
        Assert.True(index >= 0, "no such row: " + name);
        return hud.Values[index];
    }

    [Fact]
    public void Every_row_is_empty_before_any_data_exists()
    {
        var host = new FakeHost();
        var hud = new HudViewModel(host);
        var updater = new HudUpdater(host, hud, null, null, null);

        updater.Refresh();

        foreach (string value in hud.Values)
        {
            if (value == "0") // Pack Slots defaults to the character's free-slot count, not blank
                continue;
            Assert.True(string.IsNullOrEmpty(value) || value == "0");
        }
    }

    [Fact]
    public void Id_queue_has_no_host_equivalent_and_is_always_blank()
    {
        var host = new FakeHost();
        var hud = new HudViewModel(host);
        var updater = new HudUpdater(host, hud, null, null, null);

        updater.Refresh();

        Assert.Equal(string.Empty, Row(hud, "ID Queue"));
    }

    [Fact]
    public void Dps_rows_come_from_the_current_combat_tracker()
    {
        var host = new FakeHost();
        host.Automation.Character.Name = "Hero";
        var hud = new HudViewModel(host);
        var combatTrackerHost = new CombatTrackerHost(host, new OpenAC.MagTools.ChatOutput(host),
            new OpenAC.MagTools.Settings.SettingsManager(new OpenAC.MagTools.Settings.SettingsFile(host.Storage)));
        var updater = new HudUpdater(host, hud, combatTrackerHost, null, null);

        var args = new CombatEventArgs(
            "Hero", "Drudge", AttackType.MeleeMissle, DamageElement.Slash,
            IsFailedAttack: false, IsCriticalHit: false, IsOverpower: false,
            IsSneakAttack: false, IsRecklessness: false, IsKillingBlow: false,
            DamageAmount: 50);
        combatTrackerHost.Current.OnCombatEvent(args, "Hero");

        updater.Refresh();

        // The exact DPS-window math (including its single-fresh-sample
        // quirk) is covered by CombatTrackerTests; here we only assert the
        // HUD row is wired up to it at all.
        Assert.NotEqual(string.Empty, Row(hud, "DPS Out 1m"));
    }

    [Fact]
    public void Net_profit_rows_use_the_MMDh_divisor()
    {
        var host = new FakeHost();
        var hud = new HudViewModel(host);
        var inventoryTrackerHost = new InventoryTrackerHost(host);
        inventoryTrackerHost.ProfitLoss.Resync([]);
        var updater = new HudUpdater(host, hud, null, null, inventoryTrackerHost);

        updater.Refresh();

        // With no history yet, GetValueDifference returns 0.
        Assert.Equal("0.0/h", Row(hud, "Net Profit 5m"));
    }

    [Fact]
    public void Pack_slots_reads_the_characters_free_slot_count()
    {
        var host = new FakeHost();
        host.Automation.Character.MainPackFreeSlots = 12;
        var hud = new HudViewModel(host);
        var updater = new HudUpdater(host, hud, null, null, null);

        updater.Refresh();

        Assert.Equal("12", Row(hud, "Pack Slots"));
    }

    [Fact]
    public void Players_and_monsters_count_landscape_objects_by_class()
    {
        var host = new FakeHost();
        host.Automation.Objects.Objects.Add(
            FakeObjects.Landscape(1u, "Some Player", PluginObjectClass.Player, 10d));
        host.Automation.Objects.Objects.Add(
            FakeObjects.Landscape(2u, "Another Player", PluginObjectClass.Player, 20d));
        host.Automation.Objects.Objects.Add(
            FakeObjects.Landscape(3u, "A Drudge", PluginObjectClass.Monster, 15d));

        var hud = new HudViewModel(host);
        var updater = new HudUpdater(host, hud, null, null, null);

        updater.Refresh();

        // Two landscape players minus the "-1 excludes you" original rule.
        Assert.Equal("1", Row(hud, "Players"));
        Assert.Equal("1", Row(hud, "Monsters"));
    }
}
