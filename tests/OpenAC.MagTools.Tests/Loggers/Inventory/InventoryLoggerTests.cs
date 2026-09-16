using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Loggers.Inventory;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;

namespace OpenAC.MagTools.Tests.Loggers.Inventory;

public sealed class InventoryLoggerTests
{
    [Theory]
    [InlineData(PluginObjectClass.Armor, "", true)]
    [InlineData(PluginObjectClass.Clothing, "", true)]
    [InlineData(PluginObjectClass.MeleeWeapon, "", true)]
    [InlineData(PluginObjectClass.MissileWeapon, "", true)]
    [InlineData(PluginObjectClass.WandStaffOrb, "", true)]
    [InlineData(PluginObjectClass.Jewelry, "", true)]
    [InlineData(PluginObjectClass.Gem, "Coalesced Aetheria", true)]
    [InlineData(PluginObjectClass.Gem, "Some Other Gem", false)]
    [InlineData(PluginObjectClass.Misc, "Acid Phyntos Wasp Essence", true)]
    [InlineData(PluginObjectClass.Misc, "A Random Misc Item", false)]
    [InlineData(PluginObjectClass.Food, "", false)]
    public void ObjectClassNeedsIdentMatchesTheOriginalRuleSet(PluginObjectClass objectClass, string name, bool expected)
        => Assert.Equal(expected, InventoryLogger.ObjectClassNeedsIdent(objectClass, name));

    private static (FakeHost Host, ChatOutput Chat, InventoryManagementSettings Settings) Make()
    {
        var host = new FakeHost();
        var chat = new ChatOutput(host);
        var settingsFile = new SettingsFile(host.Storage);
        var settings = new InventoryManagementSettings(settingsFile);
        settings.InventoryLogger.Value = true;
        host.Automation.Character.ObjectId = 500u;
        return (host, chat, settings);
    }

    [Fact]
    public void FirstLoginWithNoFileRequestsIdsForIdentWorthyItemsAndWaits()
    {
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings) = Make();

        var sword = new PluginInventoryItem(1u, 0u, "Sword", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u, 1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0) { ObjectClass = PluginObjectClass.MeleeWeapon };
        host.Automation.Items.Owned.Add(sword);
        host.Automation.Objects.Objects.Add(new PluginWorldObject(1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u) { HasAppraisalData = false });

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream");

        Assert.Contains(1u, host.Automation.Objects.IdentifyRequests);
        Assert.Contains(host.ChatLines, line => line.Contains("Requesting id information", StringComparison.Ordinal));
        // Nothing written yet -- still waiting for id data.
        Assert.Null(host.Storage.ReadText("ACServer/Acdream.Inventory.xml"));
    }

    [Fact]
    public void OnceEveryTrackedItemHasIdDataItDumpsAndPrintsCompletion()
    {
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings) = Make();

        host.Automation.Items.Owned.Add(new PluginInventoryItem(1u, 0u, "Sword", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u, 1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0) { ObjectClass = PluginObjectClass.MeleeWeapon });
        host.Automation.Objects.Objects.Add(new PluginWorldObject(1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u) { HasAppraisalData = false });

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream");

        // Ident data arrives.
        host.Automation.Objects.Objects[0] = host.Automation.Objects.Objects[0] with { HasAppraisalData = true };
        host.Events.RaiseObjectChanged(1u, PluginObjectChangeKind.IdentReceived);

        Assert.Contains(host.ChatLines, line => line.Contains("completed. Log file written.", StringComparison.Ordinal));
        Assert.NotNull(host.Storage.ReadText("ACServer/Acdream.Inventory.xml"));
    }

    [Fact]
    public void ExistingFileDumpsImmediatelyWithoutTheWaitMessage()
    {
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings) = Make();
        host.Storage.WriteText("ACServer/Acdream.Inventory.xml", "<ArrayOfMyWorldObject></ArrayOfMyWorldObject>");

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream");

        Assert.DoesNotContain(host.ChatLines, line => line.Contains("This will take a few minutes", StringComparison.Ordinal));
    }

    [Fact]
    public void CorruptExistingFileReportsAndTreatsAsEmpty()
    {
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings) = Make();
        host.Storage.WriteText("ACServer/Acdream.Inventory.xml", "not xml <<<");

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream");

        Assert.Contains(host.ChatLines, line => line.Contains("Inventory file is corrupt.", StringComparison.Ordinal));
    }

    [Fact]
    public void StopDumpsOnLogoff()
    {
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings) = Make();
        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream");

        logger.Stop();

        Assert.NotNull(host.Storage.ReadText("ACServer/Acdream.Inventory.xml"));
    }

    [Fact]
    public void AnOwnedItemWithNoWorldObjectYetIsStillDumpedNotDropped()
    {
        // Defect #3/#5: an owned item the object table has no full
        // PluginWorldObject for yet (only the lightweight
        // PluginInventoryItem snapshot exists) used to be dropped from the
        // dump entirely instead of appearing with HasIdData=false, like the
        // original always wrote every owned item.
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings) = Make();
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            7u, 0u, "Unresolved Trinket", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.Jewelry,
        });
        // Deliberately no matching entry in host.Automation.Objects.Objects.

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream");
        logger.Stop();

        string? xml = host.Storage.ReadText("ACServer/Acdream.Inventory.xml");
        Assert.NotNull(xml);
        Assert.True(InventoryLoggerXml.TryImport(xml!, out List<MyWorldObjectRecord> records));
        MyWorldObjectRecord record = Assert.Single(records);
        Assert.Equal(7u, record.Id);
        Assert.False(record.HasIdData);
        Assert.Equal((int)PluginObjectClass.Jewelry, record.ObjectClass);

        // It also should have had an id requested for it, same as any other
        // ident-worthy owned item.
        Assert.Contains(7u, host.Automation.Objects.IdentifyRequests);
    }

    [Fact]
    public void FullLoginSequenceRequestsWaitsAndDumpsEveryOwnedItem()
    {
        // The whole §1 login sequencing the live gate found broken: print
        // the request line, request ids for the ident-worthy classes,
        // wait for ObjectChanged(IdentReceived), then dump a document that
        // includes every owned item -- appraised or not.
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings) = Make();
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            1u, 0u, "Sword", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.MeleeWeapon,
        });
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u) { HasAppraisalData = false });
        // A second owned item that will never get appraisal data this
        // session (e.g. a plain trade good) -- still must show up.
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            2u, 0u, "Trade Notes", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.Misc,
        });
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            2u, 0u, "Trade Notes", PluginObjectClass.Misc, 0u, 500u, 0u) { HasAppraisalData = true });

        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream");

        Assert.Contains(
            host.ChatLines,
            line => line.Contains("Requesting id information", StringComparison.Ordinal));
        Assert.Contains(1u, host.Automation.Objects.IdentifyRequests);
        Assert.Null(host.Storage.ReadText("ACServer/Acdream.Inventory.xml"));

        host.Automation.Objects.Objects[0] = host.Automation.Objects.Objects[0] with { HasAppraisalData = true };
        host.Events.RaiseObjectChanged(1u, PluginObjectChangeKind.IdentReceived);

        Assert.Contains(
            host.ChatLines,
            line => line.Contains("completed. Log file written.", StringComparison.Ordinal));

        string? xml = host.Storage.ReadText("ACServer/Acdream.Inventory.xml");
        Assert.NotNull(xml);
        Assert.True(InventoryLoggerXml.TryImport(xml!, out List<MyWorldObjectRecord> records));
        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.Id == 1u);
        Assert.Contains(records, r => r.Id == 2u);
    }

    [Fact]
    public void NewIdentWorthyItemInMyContainerRequestsIdOnce()
    {
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings) = Make();
        host.Storage.WriteText("ACServer/Acdream.Inventory.xml", "<ArrayOfMyWorldObject></ArrayOfMyWorldObject>");
        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream");

        host.Automation.Objects.Objects.Add(new PluginWorldObject(2u, 0u, "Ring", PluginObjectClass.Jewelry, 0u, 500u, 0u) { HasAppraisalData = false });
        host.Events.RaiseObjectChanged(2u, PluginObjectChangeKind.Created);
        host.Events.RaiseObjectChanged(2u, PluginObjectChangeKind.Created); // second delivery must not double-request

        Assert.Equal([2u], host.Automation.Objects.IdentifyRequests);
    }

    [Fact]
    public void AnItemSeenOnTheGroundStillGetsItsIdRequestedOncePickedUp()
    {
        // H7: the container check must run BEFORE _requestedIds records the
        // id, or an object first seen on the ground (not yet in my
        // container) poisons _requestedIds and never gets a real request
        // once it's actually picked up.
        (FakeHost host, ChatOutput chat, InventoryManagementSettings settings) = Make();
        host.Storage.WriteText("ACServer/Acdream.Inventory.xml", "<ArrayOfMyWorldObject></ArrayOfMyWorldObject>");
        var logger = new InventoryLogger(host, chat, settings);
        logger.Start("ACServer", "Acdream");

        // First seen on the ground: some other container (or none), not me.
        host.Automation.Objects.Objects.Add(new PluginWorldObject(3u, 0u, "Ring", PluginObjectClass.Jewelry, 0u, 0u, 0u) { HasAppraisalData = false });
        host.Events.RaiseObjectChanged(3u, PluginObjectChangeKind.Created);

        Assert.Empty(host.Automation.Objects.IdentifyRequests);

        // Now picked up into my container.
        host.Automation.Objects.Objects[0] = host.Automation.Objects.Objects[0] with { ContainerObjectId = 500u };
        host.Events.RaiseObjectChanged(3u, PluginObjectChangeKind.Created);

        Assert.Equal([3u], host.Automation.Objects.IdentifyRequests);
    }
}
