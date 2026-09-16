using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;
using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Tests.Trackers.Combat;
using Xunit;

namespace OpenAC.MagTools.Tests.Macros;

public sealed class AutoTradeAddTests
{
    private static (FakeHost Host, OpenAC.MagTools.TickScheduler Scheduler) Build(out AutoTradeAdd macro)
        => Build(out macro, null);

    private static (FakeHost Host, OpenAC.MagTools.TickScheduler Scheduler) Build(
        out AutoTradeAdd macro, TimeProvider? timeProvider)
    {
        var host = new FakeHost();
        host.LootClassifiers.Available.Add(new PluginLootClassifierInfo("plugin/moss-tank", "MossTank"));
        var lootRules = new LootRuleProcessor(host.LootClassifiers);
        var settings = new AutoTradeAddSettings(new SettingsFile(host.Storage));
        settings.Enabled.Value = true;
        var chat = new ChatOutput(host);
        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, chat);
        macro = new AutoTradeAdd(host, chat, settings, lootRules, timeProvider);
        macro.Start(scheduler);
        return (host, scheduler);
    }

    private static void MakeAppraised(FakeHost host, uint objectId, string name)
        => host.Automation.Objects.Objects.Add(new PluginWorldObject(
            objectId, 0u, name, PluginObjectClass.Misc, 0u, 0u, 0u)
        {
            HasAppraisalData = true,
        });

    [Fact]
    public void RequestsAnIdBeforeAddingAnUnappraisedItem()
    {
        (FakeHost host, OpenAC.MagTools.TickScheduler scheduler) = Build(out AutoTradeAdd macro);
        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Silver Ring"));
        host.Automation.Trade.RaiseOpened(initiatorObjectId: 100u, partnerObjectId: 2u);
        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(2u, "Bob", PluginObjectClass.Player, 0));

        scheduler.Tick(0.1);

        Assert.Contains(1u, host.Automation.Objects.IdentifyRequests);
        Assert.Empty(host.Automation.Trade.Calls);
    }

    [Fact]
    public void AddsOneKeepItemPerThinkOnceAppraised()
    {
        (FakeHost host, OpenAC.MagTools.TickScheduler scheduler) = Build(out AutoTradeAdd macro);
        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Silver Ring"));
        MakeAppraised(host, 1u, "Silver Ring");

        host.LootClassifiers.ProfileClassifyHandler = (profile, context)
            => profile == "Bob" && context.Item.Name == "Silver Ring"
                ? new PluginLootClassification(Matched: true, Action: PluginLootAction.Keep)
                : null;

        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(2u, "Bob", PluginObjectClass.Player, 0));
        host.Automation.Trade.PartnerName = "Bob";
        host.Automation.Trade.RaiseOpened(initiatorObjectId: 100u, partnerObjectId: 2u);
        scheduler.Tick(0.1);

        Assert.Contains("add:1", host.Automation.Trade.Calls);
    }

    [Fact]
    public void NeverAddsTheSameItemTwice()
    {
        (FakeHost host, OpenAC.MagTools.TickScheduler scheduler) = Build(out AutoTradeAdd macro);
        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Silver Ring"));
        MakeAppraised(host, 1u, "Silver Ring");
        host.LootClassifiers.ProfileClassifyHandler = (_, __)
            => new PluginLootClassification(Matched: true, Action: PluginLootAction.Keep);

        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(2u, "Bob", PluginObjectClass.Player, 0));
        host.Automation.Trade.RaiseOpened(initiatorObjectId: 100u, partnerObjectId: 2u);
        scheduler.Tick(0.1);
        scheduler.Tick(0.1);

        Assert.Single(host.Automation.Trade.Calls, call => call == "add:1");
    }

    [Fact]
    public void ReportsScanCompleteWhenNothingIsLeftToAdd()
    {
        (FakeHost host, OpenAC.MagTools.TickScheduler scheduler) = Build(out AutoTradeAdd macro);
        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(2u, "Bob", PluginObjectClass.Player, 0));
        host.Automation.Trade.RaiseOpened(initiatorObjectId: 100u, partnerObjectId: 2u);

        scheduler.Tick(0.1);

        Assert.Contains(
            host.ChatLines,
            line => line.Contains("Auto Add To Trade - Inventory scan complete.", StringComparison.Ordinal));
    }

    [Fact]
    public void ARefusedIdRequestIsRetriedEveryThink()
    {
        // M5: no one-shot `_idRequested` gate -- a Refused/Busy identify
        // must get another chance next think.
        (FakeHost host, OpenAC.MagTools.TickScheduler scheduler) = Build(out AutoTradeAdd macro);
        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Silver Ring"));
        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(2u, "Bob", PluginObjectClass.Player, 0));
        host.Automation.Trade.RaiseOpened(initiatorObjectId: 100u, partnerObjectId: 2u);

        scheduler.Tick(0.1);
        scheduler.Tick(0.1);
        scheduler.Tick(0.1);

        Assert.Equal(3, host.Automation.Objects.IdentifyRequests.Count(id => id == 1u));
    }

    [Fact]
    public void AnItemThatNeverGainsAnIdIsBlacklistedAfterTenSecondsSoTheScanCanComplete()
    {
        var clock = new FakeTimeProvider();
        (FakeHost host, OpenAC.MagTools.TickScheduler scheduler) = Build(out AutoTradeAdd macro, clock);
        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Cursed Ring")); // never appraised
        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(2u, "Bob", PluginObjectClass.Player, 0));
        host.Automation.Trade.RaiseOpened(initiatorObjectId: 100u, partnerObjectId: 2u);

        scheduler.Tick(0.1);
        Assert.DoesNotContain(
            host.ChatLines,
            line => line.Contains("Inventory scan complete.", StringComparison.Ordinal));

        clock.Advance(TimeSpan.FromSeconds(11));
        scheduler.Tick(0.1);

        Assert.Contains(
            host.ChatLines,
            line => line.Contains("Auto Add To Trade - Inventory scan complete.", StringComparison.Ordinal));
    }

    [Fact]
    public void StopsWhenTheTradeCloses()
    {
        (FakeHost host, OpenAC.MagTools.TickScheduler scheduler) = Build(out AutoTradeAdd macro);
        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(2u, "Bob", PluginObjectClass.Player, 0));
        host.Automation.Trade.RaiseOpened(initiatorObjectId: 100u, partnerObjectId: 2u);
        host.Automation.Trade.RaiseClosed();

        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Silver Ring"));
        MakeAppraised(host, 1u, "Silver Ring");
        host.LootClassifiers.ProfileClassifyHandler = (_, __)
            => new PluginLootClassification(Matched: true, Action: PluginLootAction.Keep);

        scheduler.Tick(0.1);

        Assert.Empty(host.Automation.Trade.Calls);
    }

    [Fact]
    public void ABusyIdentifySlotNeverBlacklistsAnItemBeforeItIsActuallyServed()
    {
        // MEDIUM (third fix round): same shape as the packer's -- the stuck
        // clock starts only once Identify() actually returns Started.
        var clock = new FakeTimeProvider();
        (FakeHost host, OpenAC.MagTools.TickScheduler scheduler) = Build(out AutoTradeAdd macro, clock);
        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Alpha Ring"));
        host.Automation.Items.Owned.Add(FakeItems.Item(2u, "Beta Ring"));
        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(3u, "Bob", PluginObjectClass.Player, 0));
        host.Automation.Objects.IdentifyResult = PluginItemCommandStatus.Refused;
        host.Automation.Trade.RaiseOpened(initiatorObjectId: 100u, partnerObjectId: 3u);

        scheduler.Tick(0.1);
        clock.Advance(TimeSpan.FromSeconds(30));
        scheduler.Tick(0.1);

        // Nothing was ever added (both still unidentified) and nothing
        // silently dropped out via a premature blacklist -- both ids are
        // still being requested every think.
        Assert.Empty(host.Automation.Trade.Calls);
        Assert.Contains(1u, host.Automation.Objects.IdentifyRequests);
        Assert.Contains(2u, host.Automation.Objects.IdentifyRequests);

        // Slot frees up for item 1 -- its clock starts now, not retroactively.
        host.Automation.Objects.IdentifyResultOverrides[1u] = PluginItemCommandStatus.Started;
        host.LootClassifiers.ProfileClassifyHandler = (_, context) => context.Item.Name switch
        {
            "Alpha Ring" => new PluginLootClassification(Matched: true, Action: PluginLootAction.Keep),
            _ => null,
        };
        scheduler.Tick(0.1);

        clock.Advance(TimeSpan.FromSeconds(5));

        // If the clock had (wrongly) started 30+ seconds ago instead of at
        // the point above, item 1 would already be blacklisted and skipped
        // here instead of appraised and added.
        MakeAppraised(host, 1u, "Alpha Ring");
        scheduler.Tick(0.1);
        Assert.Contains("add:1", host.Automation.Trade.Calls);
    }
}
