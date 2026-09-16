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
    public void AnUnappraisedItemGetsAFreshIdRequestEveryThink()
    {
        // M5: the id request must be retried every think (not one-shot) --
        // a plugin identify can legitimately come back Refused/Busy while a
        // user appraisal is in flight (see docs/plugin-api.md's Identify
        // section), so a single request that never lands must not be the
        // only chance the item ever gets.
        (FakeHost host, InventoryPacker macro, OpenAC.MagTools.TickScheduler scheduler, _) = Build();
        AllowProfile(host, "Default.AutoPack");
        host.LootClassifiers.ProfileClassificationResult = new PluginLootClassification(Matched: false, Action: PluginLootAction.NoLoot);
        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Mystery Item"));

        macro.Start();
        scheduler.Tick(0.1);
        scheduler.Tick(0.1);
        scheduler.Tick(0.1);

        Assert.Equal(3, host.Automation.Objects.IdentifyRequests.Count(id => id == 1u));
    }

    [Fact]
    public void AnItemThatNeverGainsAnIdIsBlacklistedAfterTenSecondsSoTheRunCanComplete()
    {
        // M5: an item that never becomes appraised must not pin `pendingId`
        // (and therefore the whole run) forever -- after the same 10-second
        // stuck window as everything else, it is treated as blacklisted for
        // this run so the completion message can still fire.
        (FakeHost host, InventoryPacker macro, OpenAC.MagTools.TickScheduler scheduler, FakeTimeProvider clock) = Build();
        AllowProfile(host, "Default.AutoPack");
        host.LootClassifiers.ProfileClassificationResult = new PluginLootClassification(Matched: false, Action: PluginLootAction.NoLoot);
        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Mystery Item")); // never appraised

        macro.Start();
        scheduler.Tick(0.1);
        Assert.DoesNotContain(host.ChatLines, line => line.Contains("Auto Pack - Completed.", StringComparison.Ordinal));

        clock.Advance(TimeSpan.FromSeconds(11));
        scheduler.Tick(0.1);

        Assert.Contains(host.ChatLines, line => line.Contains("Blacklisting item: 1, Mystery Item", StringComparison.Ordinal));
        Assert.Contains(host.ChatLines, line => line.Contains("Auto Pack - Completed.", StringComparison.Ordinal));
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

    [Fact]
    public void ARefusedMergeGetsBlacklistedAndAutoPackProceeds()
    {
        // H2: DoAutoStack must consult the blacklist and run the same
        // stuck timer around Merge that DoAutoPack runs around
        // MoveToContainer -- a merge the host keeps rejecting must not
        // spin forever and starve DoAutoPack of its turn.
        (FakeHost host, InventoryPacker macro, OpenAC.MagTools.TickScheduler scheduler, FakeTimeProvider clock) = Build();

        PluginInventoryItem stackA = FakeItems.Item(1u, "Arrow") with
        {
            ContainerObjectId = 1000u, MaximumStackSize = 100, StackSize = 10,
        };
        PluginInventoryItem stackB = FakeItems.Item(2u, "Arrow") with
        {
            ContainerObjectId = 1000u, MaximumStackSize = 100, StackSize = 20,
        };
        PluginInventoryItem pack1 = FakeItems.Item(10u, "Backpack") with
        {
            ObjectClass = PluginObjectClass.Container, ContainerSlot = 0, ItemsCapacity = 10,
        };
        PluginInventoryItem trophy = FakeItems.Item(3u, "Trophy") with { ContainerObjectId = 1000u };

        host.Automation.Items.Owned.Add(stackA);
        host.Automation.Items.Owned.Add(stackB);
        host.Automation.Items.Owned.Add(pack1);
        host.Automation.Items.Owned.Add(trophy);
        MakeAppraised(host, 1u, "Arrow");
        MakeAppraised(host, 2u, "Arrow");
        MakeAppraised(host, 10u, "Backpack");
        MakeAppraised(host, 3u, "Trophy");

        host.LootClassifiers.ProfileClassifyHandler = (_, context) => context.Item.Name switch
        {
            "Trophy" => new PluginLootClassification(
                Matched: true, Action: PluginLootAction.KeepUpTo, RuleName: "keep", KeepCount: 1),
            _ => new PluginLootClassification(Matched: false, Action: PluginLootAction.NoLoot),
        };

        macro.Start();
        scheduler.Tick(0.1);
        // The fake never actually mutates StackSize, so the same merge keeps
        // getting reissued -- DoAutoPack never gets a look-in while it does.
        Assert.Contains((1u, 2u, 0u), host.Automation.Items.Merges);
        Assert.Empty(host.Automation.Items.Moves);

        clock.Advance(TimeSpan.FromSeconds(11));
        scheduler.Tick(0.1);

        Assert.Contains(host.ChatLines, line => line.Contains("Blacklisting item: 1, Arrow", StringComparison.Ordinal));
        // Now that the stuck merge is blacklisted, DoAutoStack finds nothing
        // more to do and DoAutoPack gets to run in the SAME tick.
        Assert.Contains((3u, 10u, 0u, 1), host.Automation.Items.Moves);
    }

    [Fact]
    public void AWorkingItemThatIsTemporarilySetAsideGetsAFreshWindowWhenItReturns()
    {
        // M4: a single working-item/started-at pair that restarts the clock
        // the moment a DIFFERENT item becomes the one being attempted --
        // NOT a per-id dictionary that would let an item's original attempt
        // timestamp, from long before it was set aside, count against it
        // the moment it is reconsidered.
        (FakeHost host, InventoryPacker macro, OpenAC.MagTools.TickScheduler scheduler, FakeTimeProvider clock) = Build();

        PluginInventoryItem pack1 = FakeItems.Item(10u, "Backpack") with
        {
            ObjectClass = PluginObjectClass.Container, ContainerSlot = 0, ItemsCapacity = 1,
        };
        PluginInventoryItem pack2 = FakeItems.Item(20u, "Satchel") with
        {
            ObjectClass = PluginObjectClass.Container, ContainerSlot = 1, ItemsCapacity = 1,
        };
        PluginInventoryItem itemA = FakeItems.Item(1u, "Trophy") with { ContainerObjectId = 1000u };
        PluginInventoryItem itemC = FakeItems.Item(2u, "Medal") with { ContainerObjectId = 1000u };
        // Occupies pack2 at first, so item C is not yet an eligible candidate.
        PluginInventoryItem pack2Filler = FakeItems.Item(3u, "Filler") with { ContainerObjectId = 20u };

        host.Automation.Items.Owned.Add(pack1);
        host.Automation.Items.Owned.Add(pack2);
        host.Automation.Items.Owned.Add(itemA);
        host.Automation.Items.Owned.Add(itemC);
        host.Automation.Items.Owned.Add(pack2Filler);
        foreach (uint id in new[] { 10u, 20u, 1u, 2u, 3u })
            MakeAppraised(host, id, "x");

        host.LootClassifiers.ProfileClassifyHandler = (_, context) => context.Item.Name switch
        {
            "Trophy" => new PluginLootClassification(
                Matched: true, Action: PluginLootAction.KeepUpTo, RuleName: "keep", KeepCount: 1),
            "Medal" => new PluginLootClassification(
                Matched: true, Action: PluginLootAction.KeepUpTo, RuleName: "keep", KeepCount: 2),
            _ => new PluginLootClassification(Matched: false, Action: PluginLootAction.NoLoot),
        };

        macro.Start();

        // t=0: only item A is eligible (pack2 is full) -- becomes the working item.
        scheduler.Tick(0.1);
        Assert.Contains((1u, 10u, 0u, 1), host.Automation.Items.Moves);

        // t=8s: free pack2 (item C becomes eligible) and pretend item A was
        // already moved (so it drops out of the candidate list this tick) --
        // item C becomes the NEW working item, resetting the clock.
        host.Automation.Items.Owned.Remove(pack2Filler);
        int indexOfA = host.Automation.Items.Owned.FindIndex(item => item.ObjectId == 1u);
        host.Automation.Items.Owned[indexOfA] = itemA with { ContainerObjectId = 10u };
        clock.Advance(TimeSpan.FromSeconds(8));
        scheduler.Tick(0.1);
        Assert.Contains((2u, 20u, 0u, 1), host.Automation.Items.Moves);

        // t=12s (12s after item A's ORIGINAL first attempt at t=0, but only
        // 4s after the working item switched away from it): item A becomes
        // eligible again. With a per-id dictionary this would immediately
        // blacklist it (12s > the 10s window since ITS first sighting); with
        // the single working-item pair it gets a fresh window instead.
        host.Automation.Items.Owned[indexOfA] = itemA with { ContainerObjectId = 1000u };
        clock.Advance(TimeSpan.FromSeconds(4));
        host.Automation.Items.Moves.Clear();
        scheduler.Tick(0.1);

        Assert.DoesNotContain(host.ChatLines, line => line.Contains("Blacklisting item: 1,", StringComparison.Ordinal));
        Assert.Contains((1u, 10u, 0u, 1), host.Automation.Items.Moves);
    }

    [Fact]
    public void ABusyIdentifySlotNeverBlacklistsAnItemBeforeItIsActuallyServed()
    {
        // MEDIUM (third fix round): the stuck clock must start only once
        // Identify() actually returns Started -- appraisals serialize
        // through one slot, so N items stuck behind a permanently Busy slot
        // must never blacklist even well past the 10-second window, because
        // none of their requests ever really landed.
        (FakeHost host, InventoryPacker macro, OpenAC.MagTools.TickScheduler scheduler, FakeTimeProvider clock) = Build();
        AllowProfile(host, "Default.AutoPack");
        host.LootClassifiers.ProfileClassificationResult = new PluginLootClassification(Matched: false, Action: PluginLootAction.NoLoot);

        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Alpha"));
        host.Automation.Items.Owned.Add(FakeItems.Item(2u, "Beta"));
        host.Automation.Items.Owned.Add(FakeItems.Item(3u, "Gamma"));
        host.Automation.Objects.IdentifyResult = PluginItemCommandStatus.Busy;

        macro.Start();
        scheduler.Tick(0.1);

        clock.Advance(TimeSpan.FromSeconds(30));
        scheduler.Tick(0.1);
        clock.Advance(TimeSpan.FromSeconds(30));
        scheduler.Tick(0.1);

        Assert.DoesNotContain(host.ChatLines, line => line.Contains("Blacklisting item:", StringComparison.Ordinal));

        // The slot frees up: item 1's clock starts NOW, not retroactively.
        host.Automation.Objects.IdentifyResultOverrides[1u] = PluginItemCommandStatus.Started;
        scheduler.Tick(0.1);

        clock.Advance(TimeSpan.FromSeconds(5));
        scheduler.Tick(0.1);
        Assert.DoesNotContain(host.ChatLines, line => line.Contains("Blacklisting item: 1,", StringComparison.Ordinal));

        clock.Advance(TimeSpan.FromSeconds(6));
        scheduler.Tick(0.1);
        Assert.Contains(host.ChatLines, line => line.Contains("Blacklisting item: 1, Alpha", StringComparison.Ordinal));
    }

    private static void MakeAppraised(FakeHost host, uint objectId, string name)
        => host.Automation.Objects.Objects.Add(new PluginWorldObject(
            objectId, 0u, name, PluginObjectClass.Misc, 0u, 0u, 0u)
        {
            HasAppraisalData = true,
        });
}
