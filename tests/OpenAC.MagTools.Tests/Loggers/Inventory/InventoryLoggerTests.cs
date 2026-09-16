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
}
