using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;
using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using Xunit;

namespace OpenAC.MagTools.Tests.Macros;

public sealed class AutoBuySellTests
{
    private static (FakeHost Host, AutoBuySell Macro, OpenAC.MagTools.TickScheduler Scheduler) Build()
    {
        var host = new FakeHost();
        host.LootClassifiers.Available.Add(new PluginLootClassifierInfo("plugin/moss-tank", "MossTank"));
        var lootRules = new LootRuleProcessor(host.LootClassifiers);
        var settings = new AutoBuySellSettings(new SettingsFile(host.Storage));
        var chat = new ChatOutput(host);
        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, chat);
        var macro = new AutoBuySell(host, chat, settings, lootRules);
        macro.Start(scheduler);
        return (host, macro, scheduler);
    }

    private static PluginLootClassification KeepUpTo(int keepCount, string ruleName = "keep")
        => new(Matched: true, Action: PluginLootAction.KeepUpTo, RuleName: ruleName, KeepCount: keepCount);

    private static PluginLootClassification Keep(string ruleName = "keep")
        => new(Matched: true, Action: PluginLootAction.Keep, RuleName: ruleName);

    private static PluginLootClassification Sell(string ruleName = "sell")
        => new(Matched: true, Action: PluginLootAction.Sell, RuleName: ruleName);

    [Fact]
    public void KeepUpToBuysOnlyTheShortfallClampedTo5000()
    {
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler) = Build();
        host.Automation.Vendor.Items.Add(
            new PluginVendorItem(1u, 100u, "Peerless Mana Potion", PluginObjectClass.Food, 10, 1));
        host.Automation.Items.Owned.Add(FakeItems.Item(2u, "Peerless Mana Potion"));

        host.LootClassifiers.ProfileClassifyHandler = (profile, context)
            => context.Item.Name == "Peerless Mana Potion" && profile == "Fred"
                ? KeepUpTo(9999)
                : null;

        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(9u, "Fred", PluginObjectClass.Vendor, 0));
        host.Automation.Vendor.VendorName = "Fred";
        host.Automation.Vendor.RaiseOpened(9u);
        scheduler.Tick(0.1);

        // Owned count is 1, KeepCount is 9999 -> shortfall 9998, clamped to 5000.
        Assert.Contains(("addbuy:1:5000"), host.Automation.Vendor.Calls);
        Assert.Contains("buyall", host.Automation.Vendor.Calls);
    }

    [Fact]
    public void PlainKeepBuysTheMaxClampAmount()
    {
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler) = Build();
        host.Automation.Vendor.Items.Add(
            new PluginVendorItem(1u, 100u, "Prismatic Taper", PluginObjectClass.Food, 10, 1));

        host.LootClassifiers.ProfileClassifyHandler = (_, context)
            => context.Item.Name == "Prismatic Taper" ? Keep() : null;

        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(9u, "Fred", PluginObjectClass.Vendor, 0));
        host.Automation.Vendor.VendorName = "Fred";
        host.Automation.Vendor.RaiseOpened(9u);
        scheduler.Tick(0.1);

        Assert.Contains("addbuy:1:5000", host.Automation.Vendor.Calls);
    }

    [Fact]
    public void KeepUpToIsSkippedOnceOwnedCountMeetsTheThreshold()
    {
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler) = Build();
        host.Automation.Vendor.Items.Add(
            new PluginVendorItem(1u, 100u, "Peerless Mana Potion", PluginObjectClass.Food, 10, 1));
        host.Automation.Items.Owned.Add(FakeItems.Item(2u, "Peerless Mana Potion"));
        host.Automation.Items.Owned.Add(FakeItems.Item(3u, "Rusty Shortsword"));

        host.LootClassifiers.ProfileClassifyHandler = (_, context) => context.Item.Name switch
        {
            "Peerless Mana Potion" => KeepUpTo(1),
            "Rusty Shortsword" => Sell(),
            _ => null,
        };

        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(9u, "Fred", PluginObjectClass.Vendor, 0));
        host.Automation.Vendor.VendorName = "Fred";
        host.Automation.Vendor.RaiseOpened(9u);
        scheduler.Tick(0.1);

        Assert.DoesNotContain(host.Automation.Vendor.Calls, call => call.StartsWith("addbuy", StringComparison.Ordinal));
        Assert.Contains(host.ChatLines, line => line.Contains("Nothing to Buy", StringComparison.Ordinal));
        Assert.Contains("addsell:3", host.Automation.Vendor.Calls);
    }

    [Fact]
    public void SellPrefersTheFirstPlainItemOverCheaperComponentsAndTradeNotes()
    {
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler) = Build();

        PluginInventoryItem plain = FakeItems.Item(1u, "Rusty Shortsword") with { Value = 1000 };
        PluginInventoryItem cheapComponent = new PluginInventoryItem(
            2u, 0u, "Blue Snowberry", 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.SpellComponent,
            Value = 1,
        };
        host.Automation.Items.Owned.Add(cheapComponent);
        host.Automation.Items.Owned.Add(plain);

        host.LootClassifiers.ProfileClassifyHandler = (_, __) => Sell();

        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(9u, "Fred", PluginObjectClass.Vendor, 0));
        host.Automation.Vendor.VendorName = "Fred";
        host.Automation.Vendor.RaiseOpened(9u);
        scheduler.Tick(0.1);

        // Buy is empty (no vendor items configured) so the loop goes straight to sell.
        Assert.Contains("addsell:1", host.Automation.Vendor.Calls);
    }

    [Fact]
    public void SellFallsBackToCheapestNonNoteWhenNoPlainItemMatches()
    {
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler) = Build();

        PluginInventoryItem expensiveComponent = new(
            1u, 0u, "Aquamarine", 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.SpellComponent,
            Value = 500,
        };
        PluginInventoryItem cheapComponent = new(
            2u, 0u, "Blue Snowberry", 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.SpellComponent,
            Value = 1,
        };
        host.Automation.Items.Owned.Add(expensiveComponent);
        host.Automation.Items.Owned.Add(cheapComponent);

        host.LootClassifiers.ProfileClassifyHandler = (_, __) => Sell();

        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(9u, "Fred", PluginObjectClass.Vendor, 0));
        host.Automation.Vendor.VendorName = "Fred";
        host.Automation.Vendor.RaiseOpened(9u);
        scheduler.Tick(0.1);

        Assert.Contains("addsell:2", host.Automation.Vendor.Calls);
    }

    [Fact]
    public void RoundSequencesBuyThenSellAcrossTransactionCompletions()
    {
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler) = Build();
        host.Automation.Vendor.Items.Add(
            new PluginVendorItem(1u, 100u, "Prismatic Taper", PluginObjectClass.Food, 10, 1));
        host.Automation.Items.Owned.Add(FakeItems.Item(2u, "Rusty Shortsword"));

        host.LootClassifiers.ProfileClassifyHandler = (_, context) => context.Item.Name switch
        {
            "Prismatic Taper" => Keep(),
            "Rusty Shortsword" => Sell(),
            _ => null,
        };

        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(9u, "Fred", PluginObjectClass.Vendor, 0));
        host.Automation.Vendor.VendorName = "Fred";
        host.Automation.Vendor.RaiseOpened(9u);
        scheduler.Tick(0.1);

        Assert.Contains("buyall", host.Automation.Vendor.Calls);
        Assert.DoesNotContain("sellall", host.Automation.Vendor.Calls);

        host.Automation.Vendor.RaiseTransactionCompleted(PluginVendorTransactionKind.Buy);
        Assert.Contains("sellall", host.Automation.Vendor.Calls);
    }

    // Defect 14a (live-gate round 4): a refused AddToBuyList/BuyAll used to
    // be discarded outright. The macro still advanced its phase as if the
    // wire command had gone out, then waited forever for a
    // TransactionCompleted that could never arrive because none was ever
    // sent -- wedging this vendor visit's automation silently for the rest
    // of the session.
    [Fact]
    public void ARefusedBuyReportsAndStopsInsteadOfWedgingThePhase()
    {
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler) = Build();
        host.Automation.Vendor.Items.Add(
            new PluginVendorItem(1u, 100u, "Prismatic Taper", PluginObjectClass.Food, 10, 1));

        host.LootClassifiers.ProfileClassifyHandler = (_, context)
            => context.Item.Name == "Prismatic Taper" ? Keep() : null;

        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(9u, "Fred", PluginObjectClass.Vendor, 0));
        host.Automation.Vendor.VendorName = "Fred";
        host.Automation.Vendor.NextStatus = PluginVendorCommandStatus.Busy;
        host.Automation.Vendor.RaiseOpened(9u);
        scheduler.Tick(0.1);

        Assert.Contains(
            host.ChatLines,
            line => line.Contains("AutoBuySell: AddToBuyList refused: Busy", StringComparison.Ordinal));

        // The phase must not be left dangling in Buying with nothing ever
        // sent to the wire -- a later TransactionCompleted (raised by
        // something else entirely) must not be misread as this round's.
        host.Automation.Vendor.RaiseTransactionCompleted(PluginVendorTransactionKind.Buy);
        Assert.DoesNotContain("sellall", host.Automation.Vendor.Calls);
    }

    // Defect 14a: a failed (server-rejected) buy/sell transaction was
    // treated identically to a real one -- the macro proceeded to the next
    // step (selling after a "successful" buy) without ever reporting that
    // the trade actually failed. HIGH-3 (P12 review): reporting the failure
    // is now the shared VendorTransactionReporter's job (tested separately,
    // Commands/VendorTransactionReporterTests.cs) -- AutoBuySell's own
    // responsibility on a failure is to STOP instead of falling through to
    // Idle, where Think's 10 Hz cadence would re-pick the identical
    // (still-failing) item and re-issue the same buy/sell forever.
    [Fact]
    public void AFailedTransactionCompletionDeactivatesInsteadOfReIssuingTheSamePick()
    {
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler) = Build();
        host.Automation.Vendor.Items.Add(
            new PluginVendorItem(1u, 100u, "Prismatic Taper", PluginObjectClass.Food, 10, 1));

        host.LootClassifiers.ProfileClassifyHandler = (_, context)
            => context.Item.Name == "Prismatic Taper" ? Keep() : null;

        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(9u, "Fred", PluginObjectClass.Vendor, 0));
        host.Automation.Vendor.VendorName = "Fred";
        host.Automation.Vendor.RaiseOpened(9u);
        scheduler.Tick(0.1);

        int callsAfterInitialBuy = host.Automation.Vendor.Calls.Count;
        host.Automation.Vendor.RaiseTransactionCompleted(PluginVendorTransactionKind.Buy, success: false);

        // Several more Think() ticks (10 Hz cadence) must NOT re-issue
        // AddToBuyList/BuyAll for the identical pick -- the macro
        // deactivated instead of looping through Idle.
        for (int tick = 0; tick < 10; tick++)
            scheduler.Tick(0.1);

        Assert.Equal(callsAfterInitialBuy, host.Automation.Vendor.Calls.Count);
        Assert.DoesNotContain(
            host.ChatLines,
            line => line.Contains("AutoBuySell: Buy failed", StringComparison.Ordinal));
    }

    [Fact]
    public void TestModeReportsWithoutTouchingTheWire()
    {
        var host = new FakeHost();
        host.LootClassifiers.Available.Add(new PluginLootClassifierInfo("plugin/moss-tank", "MossTank"));
        var lootRules = new LootRuleProcessor(host.LootClassifiers);
        var settings = new AutoBuySellSettings(new SettingsFile(host.Storage));
        settings.TestMode.Value = true;
        var chat = new ChatOutput(host);
        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, chat);
        var macro = new AutoBuySell(host, chat, settings, lootRules);
        macro.Start(scheduler);

        host.Automation.Vendor.Items.Add(
            new PluginVendorItem(1u, 100u, "Prismatic Taper", PluginObjectClass.Food, 10, 1));
        host.LootClassifiers.ProfileClassifyHandler = (_, context)
            => context.Item.Name == "Prismatic Taper" ? Keep() : null;

        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(9u, "Fred", PluginObjectClass.Vendor, 0));
        host.Automation.Vendor.VendorName = "Fred";
        host.Automation.Vendor.RaiseOpened(9u);

        Assert.Contains(host.ChatLines, line => line.Contains("Buy Items:", StringComparison.Ordinal));
        Assert.Contains(host.ChatLines, line => line.Contains("Prismatic Taper", StringComparison.Ordinal));
        Assert.Empty(host.Automation.Vendor.Calls);

        scheduler.Tick(0.1);
        Assert.Empty(host.Automation.Vendor.Calls);
    }

    [Fact]
    public void PrintsNothingToSellWhenOnlyABuyPickExists()
    {
        // M2: "Nothing to Sell" must print on its own, independent of
        // whatever the buy side is doing.
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler) = Build();
        host.Automation.Vendor.Items.Add(
            new PluginVendorItem(1u, 100u, "Prismatic Taper", PluginObjectClass.Food, 10, 1));
        host.LootClassifiers.ProfileClassifyHandler = (_, context)
            => context.Item.Name == "Prismatic Taper" ? Keep() : null;

        host.Automation.Vendor.VendorName = "Fred";
        host.Automation.Vendor.RaiseOpened(9u);
        scheduler.Tick(0.1);

        Assert.Contains(host.ChatLines, line => line.Contains("AutoBuySell: Nothing to Sell", StringComparison.Ordinal));
        // The buy side still proceeds even though the sell side is empty.
        Assert.Contains("buyall", host.Automation.Vendor.Calls);
    }

    [Fact]
    public void BothPicksBeingTradeNotesRefusesTheRoundSilentlyWithoutTouchingTheWire()
    {
        // M1 (corrected in the third fix round): trading a note for a note
        // is never useful, so BOTH being TradeNotes refuses the round
        // SILENTLY -- no message, no wire commands. (The message instead
        // belongs to the NEITHER-is-a-TradeNote case; see the sibling test.)
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler) = Build();
        host.Automation.Vendor.Items.Add(
            new PluginVendorItem(1u, 100u, "Note of Recall", PluginObjectClass.TradeNote, 10, 1));
        PluginInventoryItem ownedNote = new(
            2u, 0u, "Note of Vitae", 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.TradeNote,
        };
        host.Automation.Items.Owned.Add(ownedNote);

        host.LootClassifiers.ProfileClassifyHandler = (_, context) => context.Item.Name switch
        {
            "Note of Recall" => Keep(),
            "Note of Vitae" => Sell(),
            _ => null,
        };

        host.Automation.Vendor.VendorName = "Fred";
        host.Automation.Vendor.RaiseOpened(9u);
        scheduler.Tick(0.1);

        Assert.DoesNotContain(
            host.ChatLines,
            line => line.Contains(
                "AutoBuySell: No TradeNotes to buy or sell. Check Loot Profile", StringComparison.Ordinal));
        Assert.Empty(host.Automation.Vendor.Calls);
    }

    [Fact]
    public void NeitherPickBeingATradeNotePrintsTheNoteMessageAndStillProceeds()
    {
        // MEDIUM (third fix round correction): upstream prints "No
        // TradeNotes to buy or sell. Check Loot Profile" when NEITHER pick
        // is a TradeNote -- it is informational, not a refusal, and the
        // round still proceeds normally.
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler) = Build();
        host.Automation.Vendor.Items.Add(
            new PluginVendorItem(1u, 100u, "Prismatic Taper", PluginObjectClass.Food, 10, 1));
        host.Automation.Items.Owned.Add(FakeItems.Item(2u, "Rusty Shortsword"));

        host.LootClassifiers.ProfileClassifyHandler = (_, context) => context.Item.Name switch
        {
            "Prismatic Taper" => Keep(),
            "Rusty Shortsword" => Sell(),
            _ => null,
        };

        host.Automation.Vendor.VendorName = "Fred";
        host.Automation.Vendor.RaiseOpened(9u);
        scheduler.Tick(0.1);

        Assert.Contains(
            host.ChatLines,
            line => line.Contains(
                "AutoBuySell: No TradeNotes to buy or sell. Check Loot Profile", StringComparison.Ordinal));
        Assert.Contains("buyall", host.Automation.Vendor.Calls);
    }

    [Fact]
    public void MixedTradeNotePickGetsNeitherTradeNoteMessage()
    {
        // Exactly one side is a TradeNote -- neither TradeNote message
        // applies, and the round proceeds.
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler) = Build();
        host.Automation.Vendor.Items.Add(
            new PluginVendorItem(1u, 100u, "Note of Recall", PluginObjectClass.TradeNote, 10, 1));
        host.Automation.Items.Owned.Add(FakeItems.Item(2u, "Rusty Shortsword"));

        host.LootClassifiers.ProfileClassifyHandler = (_, context) => context.Item.Name switch
        {
            "Note of Recall" => Keep(),
            "Rusty Shortsword" => Sell(),
            _ => null,
        };

        host.Automation.Vendor.VendorName = "Fred";
        host.Automation.Vendor.RaiseOpened(9u);
        scheduler.Tick(0.1);

        Assert.DoesNotContain(
            host.ChatLines,
            line => line.Contains(
                "AutoBuySell: No TradeNotes to buy or sell. Check Loot Profile", StringComparison.Ordinal));
        Assert.Contains("buyall", host.Automation.Vendor.Calls);
    }

    [Fact]
    public void BothPicksEmptyPrintsBothNothingMessagesBeforeStopping()
    {
        // LOW: previously the "both empty, stop the round-trip loop" branch
        // returned before either "Nothing to Buy"/"Nothing to Sell" line
        // ever printed.
        (FakeHost host, _, OpenAC.MagTools.TickScheduler scheduler) = Build();
        host.LootClassifiers.ProfileClassifyHandler = (_, __) => null;

        host.Automation.Vendor.VendorName = "Fred";
        host.Automation.Vendor.RaiseOpened(9u);
        scheduler.Tick(0.1);

        Assert.Contains(host.ChatLines, line => line.Contains("AutoBuySell: Nothing to Buy", StringComparison.Ordinal));
        Assert.Contains(host.ChatLines, line => line.Contains("AutoBuySell: Nothing to Sell", StringComparison.Ordinal));
        Assert.Empty(host.Automation.Vendor.Calls);
    }
}
