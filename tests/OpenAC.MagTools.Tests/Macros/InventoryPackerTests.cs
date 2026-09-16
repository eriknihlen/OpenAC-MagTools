using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;
using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Tests.Trackers.Combat;
using Xunit;

namespace OpenAC.MagTools.Tests.Macros;

public sealed class InventoryPackerTests
{
    private static (FakeHost Host, InventoryPacker Macro, OpenAC.MagTools.TickScheduler Scheduler, FakeTimeProvider Clock)
        Build(string characterName = "Acdream")
    {
        var host = new FakeHost();
        host.Automation.Character.Name = characterName;
        host.Automation.Character.ObjectId = 1000u;
        host.LootClassifiers.Available.Add(new PluginLootClassifierInfo("plugin/moss-tank", "MossTank"));
        var lootRules = new LootRuleProcessor(host.LootClassifiers);
        var chat = new ChatOutput(host);
        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, chat);
        var clock = new FakeTimeProvider();
        var macro = new InventoryPacker(host, chat, lootRules, clock);
        macro.Bind(scheduler);
        return (host, macro, scheduler, clock);
    }

    /// <summary>A named profile exists whenever the handler is set to answer non-null for it.</summary>
    private static void AllowProfile(FakeHost host, string profileName)
        => host.LootClassifiers.ProfileClassifyHandler = (profile, context) =>
            profile == profileName ? Classify(host, context) : null;

    /// <summary>Overridden per test via a second handler layer; default: no match.</summary>
    private static PluginLootClassification? Classify(FakeHost host, PluginLootClassificationContext context)
        => host.LootClassifiers.ProfileClassificationResult;

    [Fact]
    public void SilentNoOpWhenNeitherProfileExists()
    {
        (FakeHost host, InventoryPacker macro, _, _) = Build();
        // No handler configured at all -- ProfileExists() returns false for both names.
        macro.Start();

        Assert.False(macro.IsRunning);
        Assert.Empty(host.ChatLines);
    }

    [Fact]
    public void PrefersTheCharacterScopedProfileOverDefault()
    {
        (FakeHost host, InventoryPacker macro, OpenAC.MagTools.TickScheduler scheduler, _) = Build();
        var seen = new List<string>();
        host.LootClassifiers.ProfileClassifyHandler = (profile, _) =>
        {
            seen.Add(profile);
            return profile == "Acdream.AutoPack"
                ? new PluginLootClassification(Matched: false, Action: PluginLootAction.NoLoot)
                : null;
        };

        macro.Start();

        Assert.True(macro.IsRunning);
        Assert.Contains("Acdream.AutoPack", seen);
        Assert.Contains(host.ChatLines, line => line.Contains("Auto Pack - Started.", StringComparison.Ordinal));
    }

    [Fact]
    public void FallsBackToDefaultProfileWhenCharacterProfileIsMissing()
    {
        (FakeHost host, InventoryPacker macro, _, _) = Build();
        host.LootClassifiers.ProfileClassifyHandler = (profile, _) =>
            profile == "Default.AutoPack" ? new PluginLootClassification(Matched: false, Action: PluginLootAction.NoLoot) : null;

        macro.Start();

        Assert.True(macro.IsRunning);
    }

    [Fact]
    public void AutoStackMergesTwoSameNameStacksInTheSameContainer()
    {
        (FakeHost host, InventoryPacker macro, OpenAC.MagTools.TickScheduler scheduler, _) = Build();
        AllowProfile(host, "Default.AutoPack");
        host.LootClassifiers.ProfileClassificationResult = new PluginLootClassification(Matched: false, Action: PluginLootAction.NoLoot);

        PluginInventoryItem first = FakeItems.Item(1u, "Arrow") with
        {
            ContainerObjectId = 1000u, MaximumStackSize = 100, StackSize = 10,
        };
        PluginInventoryItem second = FakeItems.Item(2u, "Arrow") with
        {
            ContainerObjectId = 1000u, MaximumStackSize = 100, StackSize = 20,
        };
        host.Automation.Items.Owned.Add(first);
        host.Automation.Items.Owned.Add(second);
        MakeAppraised(host, 1u, "Arrow");
        MakeAppraised(host, 2u, "Arrow");

        macro.Start();
        scheduler.Tick(0.1);

        Assert.Contains((1u, 2u, 0u), host.Automation.Items.Merges);
    }

    [Fact]
    public void AutoPackReadsKeepCountDigitByDigitAsPackPriority()
    {
        (FakeHost host, InventoryPacker macro, OpenAC.MagTools.TickScheduler scheduler, _) = Build();

        // Pack 1 = a side container at ContainerSlot 0, full (0 free slots).
        // Pack 2 = a side container at ContainerSlot 1, with room.
        PluginInventoryItem pack1 = FakeItems.Item(10u, "Backpack") with
        {
            ObjectClass = PluginObjectClass.Container, ContainerSlot = 0, ItemsCapacity = 1,
        };
        PluginInventoryItem pack2 = FakeItems.Item(11u, "Satchel") with
        {
            ObjectClass = PluginObjectClass.Container, ContainerSlot = 1, ItemsCapacity = 10,
        };
        PluginInventoryItem existingInPack1 = FakeItems.Item(12u, "Filler") with { ContainerObjectId = 10u };

        PluginInventoryItem target = FakeItems.Item(1u, "Trophy") with { ContainerObjectId = 1000u };

        host.Automation.Items.Owned.Add(pack1);
        host.Automation.Items.Owned.Add(pack2);
        host.Automation.Items.Owned.Add(existingInPack1);
        host.Automation.Items.Owned.Add(target);
        MakeAppraised(host, 10u, "Backpack");
        MakeAppraised(host, 11u, "Satchel");
        MakeAppraised(host, 12u, "Filler");
        MakeAppraised(host, 1u, "Trophy");

        host.LootClassifiers.ProfileClassifyHandler = (_, context) => context.Item.Name switch
        {
            // Data1 = 12 -> try pack 1 first, then pack 2.
            "Trophy" => new PluginLootClassification(
                Matched: true, Action: PluginLootAction.KeepUpTo, RuleName: "keep", KeepCount: 12),
            _ => new PluginLootClassification(Matched: false, Action: PluginLootAction.NoLoot),
        };

        macro.Start();
        scheduler.Tick(0.1);

        // Pack 1 is full, so the item lands in pack 2 (container id 11).
        Assert.Contains((1u, 11u, 0u, 1), host.Automation.Items.Moves);
    }

    [Fact]
    public void ItemAlreadyInItsPrimaryPackIsSkipped()
    {
        (FakeHost host, InventoryPacker macro, OpenAC.MagTools.TickScheduler scheduler, _) = Build();
        PluginInventoryItem pack1 = FakeItems.Item(10u, "Backpack") with
        {
            ObjectClass = PluginObjectClass.Container, ContainerSlot = 0, ItemsCapacity = 10,
        };
        PluginInventoryItem target = FakeItems.Item(1u, "Trophy") with { ContainerObjectId = 10u };

        host.Automation.Items.Owned.Add(pack1);
        host.Automation.Items.Owned.Add(target);
        MakeAppraised(host, 10u, "Backpack");
        MakeAppraised(host, 1u, "Trophy");

        host.LootClassifiers.ProfileClassifyHandler = (_, context) => context.Item.Name switch
        {
            "Trophy" => new PluginLootClassification(
                Matched: true, Action: PluginLootAction.KeepUpTo, RuleName: "keep", KeepCount: 1),
            _ => new PluginLootClassification(Matched: false, Action: PluginLootAction.NoLoot),
        };

        macro.Start();
        scheduler.Tick(0.1);

        Assert.Empty(host.Automation.Items.Moves);
        Assert.Contains(host.ChatLines, line => line.Contains("Auto Pack - Completed.", StringComparison.Ordinal));
    }

    [Fact]
    public void RequestsIdBeforeClassifyingAnUnappraisedItem()
    {
        (FakeHost host, InventoryPacker macro, OpenAC.MagTools.TickScheduler scheduler, _) = Build();
        AllowProfile(host, "Default.AutoPack");
        host.LootClassifiers.ProfileClassificationResult = new PluginLootClassification(Matched: false, Action: PluginLootAction.NoLoot);

        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Mystery Item"));

        macro.Start();
        scheduler.Tick(0.1);

        Assert.Contains(1u, host.Automation.Objects.IdentifyRequests);
    }

    [Fact]
    public void BlacklistsByIdAfterTenSecondsStuck()
    {
        (FakeHost host, InventoryPacker macro, OpenAC.MagTools.TickScheduler scheduler, FakeTimeProvider clock) = Build();

        PluginInventoryItem pack1 = FakeItems.Item(10u, "Backpack") with
        {
            ObjectClass = PluginObjectClass.Container, ContainerSlot = 0, ItemsCapacity = 10,
        };
        PluginInventoryItem target = FakeItems.Item(1u, "Trophy") with { ContainerObjectId = 1000u };
        host.Automation.Items.Owned.Add(pack1);
        host.Automation.Items.Owned.Add(target);
        MakeAppraised(host, 10u, "Backpack");
        MakeAppraised(host, 1u, "Trophy");

        host.LootClassifiers.ProfileClassifyHandler = (_, context) => context.Item.Name switch
        {
            "Trophy" => new PluginLootClassification(
                Matched: true, Action: PluginLootAction.KeepUpTo, RuleName: "keep", KeepCount: 1),
            _ => new PluginLootClassification(Matched: false, Action: PluginLootAction.NoLoot),
        };

        macro.Start();
        scheduler.Tick(0.1);
        Assert.Contains((1u, 10u, 0u, 1), host.Automation.Items.Moves);

        // The move never "took" from the fake's point of view (ContainerObjectId
        // does not change), so the packer keeps re-selecting it every think.
        clock.Advance(TimeSpan.FromSeconds(11));
        scheduler.Tick(0.1);

        Assert.Contains(host.ChatLines, line => line.Contains("Blacklisting item: 1, Trophy", StringComparison.Ordinal));
    }

    private static void MakeAppraised(FakeHost host, uint objectId, string name)
        => host.Automation.Objects.Objects.Add(new PluginWorldObject(
            objectId, 0u, name, PluginObjectClass.Misc, 0u, 0u, 0u)
        {
            HasAppraisalData = true,
        });
}
