using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Chat;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Trackers.Combat;

namespace OpenAC.MagTools.Tests.Trackers.Combat;

public sealed class CombatTrackerHostTests
{
    private static (FakeHost Host, SettingsManager Settings, CombatTrackerHost CombatHost) Build()
    {
        var host = new FakeHost();
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var combatHost = new CombatTrackerHost(host, new ChatOutput(host), settings);
        host.Automation.Character.Name = "Acdream";
        return (host, settings, combatHost);
    }

    [Fact]
    public void ChatKindLinesNeverReachTheTrackersEvenWhenCombatShaped()
    {
        (FakeHost host, _, CombatTrackerHost combatHost) = Build();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        combatHost.Start(scheduler, "Frostfell", "Acdream");

        // Player speech that happens to be combat-shaped text must never
        // reach the parsers — IsChat excludes it regardless of Kind.
        host.Say("Bob", "You obliterate Drudge Skulker!", mine: true);
        Assert.Empty(combatHost.Current.CombatInfos);

        host.Combat("You obliterate Drudge Skulker!");
        Assert.Single(combatHost.Current.CombatInfos);
    }

    [Fact]
    public void SystemKindCombatAdjacentLinesAlsoReachTheTrackers()
    {
        // Only evade/damage/kill lines the host itself composes arrive with
        // Kind == Combat. Everything else combat-adjacent (aetheria surges,
        // cloak surges, resists, spell fizzles) rides the host's generic
        // text path and arrives with Kind == System — see the OnClassified
        // remarks and docs/deviations.md. Feeding only Combat would starve
        // AetheriaTracker/CloakTracker entirely.
        (FakeHost host, _, CombatTrackerHost combatHost) = Build();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        combatHost.Start(scheduler, "Frostfell", "Acdream");

        host.System("Aetheria surges on Acdream with the power of Surge of Destruction!");

        Assert.Single(combatHost.Current.AetheriaInfos);
        Assert.Single(combatHost.Persistent.AetheriaInfos);
    }

    [Fact]
    public void OneChatLineFeedsBothCurrentAndPersistentTrackers()
    {
        (FakeHost host, _, CombatTrackerHost combatHost) = Build();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        combatHost.Start(scheduler, "Frostfell", "Acdream");

        host.Combat("You scorch Annihilator for 685 points of fire damage!");

        Assert.Single(combatHost.Current.CombatInfos);
        Assert.Single(combatHost.Persistent.CombatInfos);
    }

    [Fact]
    public void UnparseableCombatLineWritesTheOriginalDiagnosticText()
    {
        (FakeHost host, _, CombatTrackerHost combatHost) = Build();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        combatHost.Start(scheduler, "Frostfell", "Acdream");

        // Looks like an attack (has "for N points of X damage") but the verb
        // is not a recognized element substring, and importantly it must
        // still bind to the local player to be considered at all.
        host.Combat("You zibber Reviath for 12 points of qqqzzz damage!");

        Assert.Contains(
            host.ChatLines,
            line => line.Contains(
                "Unable to parse damage element from:", StringComparison.Ordinal));
    }

    [Fact]
    public void UnparseableCombatLineStaysQuietWhenDebuggingIsDisabled()
    {
        (FakeHost host, SettingsManager settings, CombatTrackerHost combatHost) = Build();
        settings.Misc.DebuggingEnabled.Value = false;
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        combatHost.Start(scheduler, "Frostfell", "Acdream");

        host.Combat("You zibber Reviath for 12 points of qqqzzz damage!");

        Assert.DoesNotContain(
            host.ChatLines,
            line => line.Contains(
                "Unable to parse damage element from:", StringComparison.Ordinal));
    }

    [Fact]
    public void StopUnsubscribesSoLaterLinesAreNotTracked()
    {
        (FakeHost host, _, CombatTrackerHost combatHost) = Build();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        combatHost.Start(scheduler, "Frostfell", "Acdream");
        combatHost.Stop();

        host.Combat("You obliterate Drudge Skulker!");

        Assert.Empty(combatHost.Current.CombatInfos);
    }

    [Fact]
    public void TenMinuteSaveCadenceWritesThePersistentKeyWhenPersistentIsOn()
    {
        (FakeHost host, SettingsManager settings, CombatTrackerHost combatHost) = Build();
        settings.CombatTracker.Persistent.Value = true;
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        combatHost.Start(scheduler, "Frostfell", "Acdream");

        host.Combat("You obliterate Drudge Skulker!");

        const string key = "Frostfell/Acdream.CombatTracker.xml";
        Assert.Null(host.Storage.ReadText(key));

        // Under 10 minutes: no save yet.
        scheduler.Tick(9 * 60);
        Assert.Null(host.Storage.ReadText(key));

        // At 10 minutes: the persistent save fires.
        scheduler.Tick(60);
        Assert.NotNull(host.Storage.ReadText(key));
    }

    [Fact]
    public void SaveCadenceDoesNothingWhenPersistentIsOff()
    {
        (FakeHost host, SettingsManager settings, CombatTrackerHost combatHost) = Build();
        settings.CombatTracker.Persistent.Value = false;
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        combatHost.Start(scheduler, "Frostfell", "Acdream");

        host.Combat("You obliterate Drudge Skulker!");
        scheduler.Tick(10 * 60);

        Assert.Null(host.Storage.ReadText("Frostfell/Acdream.CombatTracker.xml"));
    }

    [Fact]
    public void StartImportsThePersistentFileWhenPersistentIsOn()
    {
        var host = new FakeHost();
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        settings.CombatTracker.Persistent.Value = true;
        host.Automation.Character.Name = "Acdream";

        var info = new CombatInfo("Acdream", "Drudge") { KillingBlows = 5 };
        string xml = CombatTrackerExporter.Export([info], [], [])!;
        host.Storage.WriteText("Frostfell/Acdream.CombatTracker.xml", xml);

        var combatHost = new CombatTrackerHost(host, new ChatOutput(host), settings);
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        combatHost.Start(scheduler, "Frostfell", "Acdream");

        CombatInfo imported = Assert.Single(combatHost.Persistent.CombatInfos);
        Assert.Equal(5, imported.KillingBlows);
        // The current-session tracker starts empty regardless of import.
        Assert.Empty(combatHost.Current.CombatInfos);
    }

    [Fact]
    public void StopExportsCurrentStatsOnLogOffWhenTheSettingIsOn()
    {
        (FakeHost host, SettingsManager settings, CombatTrackerHost combatHost) = Build();
        settings.CombatTracker.ExportOnLogOff.Value = true;
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        combatHost.Start(scheduler, "Frostfell", "Acdream");
        host.Combat("You obliterate Drudge Skulker!");

        combatHost.Stop();

        Assert.Contains(
            host.ChatLines, line => line.Contains("Stats exported to:", StringComparison.Ordinal));
        Assert.Contains(
            host.Storage.List("Frostfell/"),
            key => key.StartsWith("Frostfell/Acdream.CombatTracker.", StringComparison.Ordinal)
                && key.EndsWith(".xml", StringComparison.Ordinal)
                && key != "Frostfell/Acdream.CombatTracker.xml");
    }

    [Fact]
    public void StopDoesNotExportWhenTheSettingIsOff()
    {
        (FakeHost host, SettingsManager settings, CombatTrackerHost combatHost) = Build();
        settings.CombatTracker.ExportOnLogOff.Value = false;
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        combatHost.Start(scheduler, "Frostfell", "Acdream");
        host.Combat("You obliterate Drudge Skulker!");

        combatHost.Stop();

        Assert.DoesNotContain(
            host.ChatLines, line => line.Contains("Stats exported to:", StringComparison.Ordinal));
    }

    [Fact]
    public void ClearCurrentAndClearPersistentOnlyAffectTheirOwnTracker()
    {
        (FakeHost host, _, CombatTrackerHost combatHost) = Build();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        combatHost.Start(scheduler, "Frostfell", "Acdream");
        host.Combat("You obliterate Drudge Skulker!");

        combatHost.ClearCurrent();

        Assert.Empty(combatHost.Current.CombatInfos);
        Assert.Single(combatHost.Persistent.CombatInfos);
    }
}
