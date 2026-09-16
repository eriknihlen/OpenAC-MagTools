using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;
using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Tests.Trackers.Combat;
using Xunit;

namespace OpenAC.MagTools.Tests.Macros;

public sealed class LooterTests
{
    private static (FakeHost Host, Looter Macro, OpenAC.MagTools.TickScheduler Scheduler, FakeTimeProvider Clock)
        Build()
    {
        var host = new FakeHost();
        host.LootClassifiers.Available.Add(new PluginLootClassifierInfo("plugin/moss-tank", "MossTank"));
        var lootRules = new LootRuleProcessor(host.LootClassifiers);
        var settings = new LootingSettings(new SettingsFile(host.Storage));
        var chat = new ChatOutput(host);
        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, chat);
        var clock = new FakeTimeProvider();
        var macro = new Looter(host, chat, settings, lootRules, clock);
        macro.Start(scheduler);
        return (host, macro, scheduler, clock);
    }

    private static void OpenContainer(FakeHost host, uint containerId, string name, PluginObjectClass objectClass)
    {
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            containerId, 0u, name, objectClass, 0u, 0u, 0u));
        host.Automation.Objects.OpenContainerObjectId = containerId;
        host.Events.RaiseContainerOpened(containerId);
    }

    [Fact]
    public void IgnoresAContainerNamedExactlyStorage()
    {
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler, _) = Build();
        OpenContainer(host, 5u, "Storage", PluginObjectClass.Container);
        host.Automation.Loot.Contents.Add(FakeItems.Item(1u, "Anything"));

        scheduler.Tick(0.1);

        Assert.Empty(host.Automation.Loot.PickedUp);
    }

    [Fact]
    public void LootsACorpseWhenAutoLootCorpsesIsOn()
    {
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler, _) = Build();
        OpenContainer(host, 5u, "Corpse of a Drudge", PluginObjectClass.Corpse);
        host.Automation.Loot.Contents.Add(new PluginWorldObjectItem(1u, "Loot", PluginObjectClass.Misc).ToItem());

        host.LootClassifiers.ClassifyHandler = _
            => new PluginLootClassification(Matched: true, Action: PluginLootAction.Keep);

        scheduler.Tick(0.1);

        Assert.Contains(1u, host.Automation.Loot.PickedUp);
    }

    [Fact]
    public void MyOwnCorpseLootsUnconditionallyWithoutClassifying()
    {
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler, _) = Build();
        host.Automation.Character.Name = "Acdream";
        OpenContainer(host, 5u, "Corpse of Acdream", PluginObjectClass.Corpse);
        host.Automation.Loot.Contents.Add(new PluginWorldObjectItem(1u, "Anything", PluginObjectClass.Misc).ToItem());

        // No classifier configured (returns false/no match) -- own-corpse
        // loot skips rule checks entirely.
        scheduler.Tick(0.1);

        Assert.Contains(1u, host.Automation.Loot.PickedUp);
    }

    [Fact]
    public void SkipsSalvageUnlessLootSalvageIsOn()
    {
        var host = new FakeHost();
        host.LootClassifiers.Available.Add(new PluginLootClassifierInfo("plugin/moss-tank", "MossTank"));
        var lootRules = new LootRuleProcessor(host.LootClassifiers);
        var settings = new LootingSettings(new SettingsFile(host.Storage));
        settings.LootSalvage.Value = false;
        var chat = new ChatOutput(host);
        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, chat);
        var macro = new Looter(host, chat, settings, lootRules);
        macro.Start(scheduler);

        OpenContainer(host, 5u, "Corpse of a Drudge", PluginObjectClass.Corpse);
        host.Automation.Loot.Contents.Add(new PluginWorldObjectItem(1u, "Salvage Bag", PluginObjectClass.Salvage).ToItem());
        host.LootClassifiers.ClassifyHandler = _
            => new PluginLootClassification(Matched: true, Action: PluginLootAction.Salvage);

        scheduler.Tick(0.1);

        Assert.Empty(host.Automation.Loot.PickedUp);
    }

    [Fact]
    public void KeepUpToSkipsOnceOwnedCountMeetsTheThreshold()
    {
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler, _) = Build();
        OpenContainer(host, 5u, "Corpse of a Drudge", PluginObjectClass.Corpse);
        host.Automation.Loot.Contents.Add(new PluginWorldObjectItem(1u, "Arrow", PluginObjectClass.MissileWeapon).ToItem());
        host.Automation.Items.Owned.Add(FakeItems.Item(2u, "Arrow"));

        host.LootClassifiers.ClassifyHandler = context => context.Item.Name == "Arrow"
            ? new PluginLootClassification(Matched: true, Action: PluginLootAction.KeepUpTo, RuleName: "keep", KeepCount: 1)
            : null;

        scheduler.Tick(0.1);

        Assert.Empty(host.Automation.Loot.PickedUp);
    }

    [Fact]
    public void BlacklistsByNameAfterTenSecondsStuck()
    {
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler, FakeTimeProvider clock) = Build();
        OpenContainer(host, 5u, "Corpse of a Drudge", PluginObjectClass.Corpse);
        host.Automation.Loot.Contents.Add(new PluginWorldObjectItem(1u, "Stuck Item", PluginObjectClass.Misc).ToItem());

        host.LootClassifiers.ClassifyHandler = _
            => new PluginLootClassification(Matched: true, Action: PluginLootAction.Keep);

        scheduler.Tick(0.1);
        Assert.Contains(1u, host.Automation.Loot.PickedUp);

        clock.Advance(TimeSpan.FromSeconds(11));
        scheduler.Tick(0.1);

        Assert.Contains(host.ChatLines, line => line.Contains("Blacklisting item: 1, Stuck Item", StringComparison.Ordinal));
    }

    [Fact]
    public void PrintsNoMoreLootableItemsOnlyForNonCorpseContainers()
    {
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler, _) = Build();
        OpenContainer(host, 5u, "Treasure Chest", PluginObjectClass.Container);
        host.Automation.Loot.Contents.Add(new PluginWorldObjectItem(1u, "Junk", PluginObjectClass.Misc).ToItem());
        host.LootClassifiers.ClassifyHandler = _
            => new PluginLootClassification(Matched: true, Action: PluginLootAction.NoLoot);

        scheduler.Tick(0.1);

        Assert.Contains(host.ChatLines, line => line.Contains("No more lootable items found.", StringComparison.Ordinal));
    }

    [Fact]
    public void AnItemThatNeverGainsAnIdDoesNotStallLootingOfOtherItems()
    {
        // H1: the id-request pass used to `return` right after issuing a
        // request, which meant one item that never becomes identified
        // stalled the WHOLE run -- PickNext (the loot pass) never even ran.
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler, _) = Build();
        OpenContainer(host, 5u, "Treasure Chest", PluginObjectClass.Container);

        PluginInventoryItem stuckItem = new PluginWorldObjectItem(1u, "Mystery Box", PluginObjectClass.Misc).ToItem();
        PluginInventoryItem readyItem = new PluginWorldObjectItem(2u, "Rusty Shortsword", PluginObjectClass.Misc).ToItem();
        host.Automation.Loot.Contents.Add(stuckItem);
        host.Automation.Loot.Contents.Add(readyItem);

        // Item 1 always reports "still needs an id" (simulating an identify
        // that keeps coming back Refused/Busy); item 2 never needs one.
        host.LootClassifiers.NeedsIdentificationHandler = context => context.Item.ObjectId == 1u;
        host.LootClassifiers.ClassifyHandler = context => context.Item.ObjectId == 2u
            ? new PluginLootClassification(Matched: true, Action: PluginLootAction.Keep)
            : null;

        scheduler.Tick(0.1);

        // The stuck item gets an id request...
        Assert.Contains(1u, host.Automation.Loot.IdentifyRequests);
        // ... but item 2 still gets picked up in the SAME tick, not stalled behind it.
        Assert.Contains(2u, host.Automation.Loot.PickedUp);

        // Once the ready item is gone (as if it had actually been removed
        // from the container after being picked up) and only the perpetually
        // unidentified item remains, the run completes rather than hanging.
        host.Automation.Loot.Contents.Remove(readyItem);
        host.Automation.Loot.PickedUp.Clear();
        scheduler.Tick(0.1);

        Assert.Empty(host.Automation.Loot.PickedUp);
        Assert.Contains(host.ChatLines, line => line.Contains("No more lootable items found.", StringComparison.Ordinal));
    }

    [Fact]
    public void RequestsIdsEvenWhileBusyButSkipsPickupUntilNotBusy()
    {
        // LOW: matches upstream ordering -- the id-request pass runs even
        // while Loot.IsBusy; only classify/pickup waits for it to clear.
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler, _) = Build();
        OpenContainer(host, 5u, "Treasure Chest", PluginObjectClass.Container);
        host.Automation.Loot.Contents.Add(new PluginWorldObjectItem(1u, "Ring", PluginObjectClass.Jewelry).ToItem());
        host.LootClassifiers.NeedsIdentificationHandler = _ => true;
        host.Automation.Loot.IsBusy = true;

        scheduler.Tick(0.1);

        Assert.Contains(1u, host.Automation.Loot.IdentifyRequests);
        Assert.Empty(host.Automation.Loot.PickedUp);
    }

    [Fact]
    public void ChestNameMatchStartsEvenWhenTheContainerIsClassClassifiedAsACorpseAndCorpseLootingIsOff()
    {
        // LOW: the original's chest-name check carries no class restriction
        // of its own -- this port does not invent an !isCorpse guard.
        // AutoLootCorpses/AutoLootMyCorpses are both off, so ONLY the
        // AutoLootChests branch can be what starts this run.
        var host = new FakeHost();
        host.LootClassifiers.Available.Add(new PluginLootClassifierInfo("plugin/moss-tank", "MossTank"));
        var lootRules = new LootRuleProcessor(host.LootClassifiers);
        var settings = new LootingSettings(new SettingsFile(host.Storage));
        settings.AutoLootCorpses.Value = false;
        settings.AutoLootMyCorpses.Value = false;
        var chat = new ChatOutput(host);
        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, chat);
        var macro = new Looter(host, chat, settings, lootRules);
        macro.Start(scheduler);

        OpenContainer(host, 5u, "Corpse of a Reliquary Guardian", PluginObjectClass.Corpse);
        host.Automation.Loot.Contents.Add(new PluginWorldObjectItem(1u, "Loot", PluginObjectClass.Misc).ToItem());
        host.LootClassifiers.ClassifyHandler = _
            => new PluginLootClassification(Matched: true, Action: PluginLootAction.Keep);

        scheduler.Tick(0.1);

        Assert.Contains(1u, host.Automation.Loot.PickedUp);
    }

    /// <summary>Tiny helper so a test can build a corpse-content item without repeating the 27-arg constructor.</summary>
    private readonly record struct PluginWorldObjectItem(uint ObjectId, string Name, PluginObjectClass ObjectClass)
    {
        public PluginInventoryItem ToItem() => LootRuleProcessor.BuildMinimalItem(ObjectId, 0u, Name, ObjectClass);
    }
}
