using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Inventory;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Tests.ItemInfo;

namespace OpenAC.MagTools.Tests.Inventory;

public sealed class InventoryExporterTests
{
    private static (FakeHost Host, SettingsManager Settings, TickScheduler Scheduler, InventoryExporter Exporter)
        Build()
    {
        var host = new FakeHost();
        host.Automation.Character.ObjectId = 1u;
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        var exporter = new InventoryExporter(
            host, new ChatOutput(host), settings.ItemInfoOnIdent, new FakeFormatterSpellCatalog(), scheduler);
        return (host, settings, scheduler, exporter);
    }

    private static void AddOwnedSword(FakeHost host, uint id, bool identified)
    {
        host.Automation.Items.Owned.Add(FakeItems.Item(id, "Sword") with
        {
            ObjectClass = PluginObjectClass.MeleeWeapon,
            ContainerObjectId = host.Automation.Character.ObjectId,
        });
        host.Automation.Objects.Replace(new PluginWorldObject(
            id, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, host.Automation.Character.ObjectId, 0u)
        {
            HasAppraisalData = identified,
        });
    }

    [Fact]
    public void ExportRequestsIdsForUnidentifiedIdentWorthyItemsBeforeWriting()
    {
        (FakeHost host, _, TickScheduler scheduler, InventoryExporter exporter) = Build();
        AddOwnedSword(host, 101u, identified: false);

        exporter.ExportToClipboard(ExportGroups.Inventory);

        host.Events.RaiseTick(0.1); // request pass
        Assert.Contains(101u, host.Automation.Objects.IdentifyRequests);
        Assert.Empty(host.Clipboard.Written); // not written yet, still waiting

        // Item becomes identified; next think should export and stop.
        host.Automation.Objects.Replace(new PluginWorldObject(
            101u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 1u, 0u) { HasAppraisalData = true });
        host.Events.RaiseTick(0.1);

        Assert.Single(host.Clipboard.Written);
        Assert.Contains("Sword", host.Clipboard.Written[0]);
    }

    [Fact]
    public void ExportOfAlreadyIdentifiedItemsWritesOnTheSecondThink()
    {
        (FakeHost host, _, TickScheduler scheduler, InventoryExporter exporter) = Build();
        AddOwnedSword(host, 101u, identified: true);

        exporter.ExportToClipboard(ExportGroups.Inventory);
        host.Events.RaiseTick(0.1); // request pass (idsRequested becomes true, no waits since already identified)
        host.Events.RaiseTick(0.1); // export pass

        Assert.Single(host.Clipboard.Written);
    }

    [Fact]
    public void WornEquipmentGroupOnlyExportsEquippedItems()
    {
        // Defect 9 fixture shape: AppAutomationSurface (see
        // src/AcDream.App/Plugins/AppAutomationSurface.cs and
        // src/AcDream.Plugin.Abstractions/ItemAutomation.cs's
        // PluginInventoryItem) sets ContainerSlot == -1 on EVERY equipped
        // item -- not just weapons like the original Decal assumption
        // required -- and puts the wielding entity's id in WielderObjectId,
        // not ContainerObjectId. An equipped item's ContainerObjectId is
        // whatever container it was equipped FROM (or 0), never the player.
        (FakeHost host, _, _, InventoryExporter exporter) = Build();
        host.Automation.Items.Owned.Add(FakeItems.Item(101u, "Equipped Sword", equippedLocation: 1u) with
        {
            ObjectClass = PluginObjectClass.MeleeWeapon,
            ContainerObjectId = 0u,
            WielderObjectId = host.Automation.Character.ObjectId,
            ContainerSlot = -1,
        });
        host.Automation.Objects.Replace(new PluginWorldObject(
            101u, 0u, "Equipped Sword", PluginObjectClass.MeleeWeapon, 0u, 1u, 0u) { HasAppraisalData = true });
        host.Automation.Items.Owned.Add(FakeItems.Item(102u, "Loose Dagger") with
        {
            ObjectClass = PluginObjectClass.MeleeWeapon,
            ContainerObjectId = host.Automation.Character.ObjectId,
        });
        host.Automation.Objects.Replace(new PluginWorldObject(
            102u, 0u, "Loose Dagger", PluginObjectClass.MeleeWeapon, 0u, 1u, 0u) { HasAppraisalData = true });

        exporter.ExportToClipboard(ExportGroups.WornEquipment);
        host.Events.RaiseTick(0.1);
        host.Events.RaiseTick(0.1);

        Assert.Single(host.Clipboard.Written);
        Assert.Contains("Equipped Sword", host.Clipboard.Written[0]);
        Assert.DoesNotContain("Loose Dagger", host.Clipboard.Written[0]);
    }

    [Fact]
    public void PrintsStartAndCompletionMessages()
    {
        (FakeHost host, _, _, InventoryExporter exporter) = Build();
        AddOwnedSword(host, 101u, identified: true);

        exporter.ExportToClipboard(ExportGroups.Inventory);
        host.Events.RaiseTick(0.1);
        host.Events.RaiseTick(0.1);

        Assert.Contains(
            host.ChatLines, line => line.Contains("Copying all inventory item info to clipboard"));
        Assert.Contains(
            host.ChatLines, line => line.Contains("All inventory item info has been copied to the clipboard"));
    }

    [Fact]
    public void WhenTheClipboardRefusesTheWriteThePlacementMessageIsPrintedNotTheSuccessOne()
    {
        (FakeHost host, _, _, InventoryExporter exporter) = Build();
        AddOwnedSword(host, 101u, identified: true);
        host.Clipboard.Available = false;

        exporter.ExportToClipboard(ExportGroups.Inventory);
        host.Events.RaiseTick(0.1);
        host.Events.RaiseTick(0.1);

        Assert.Empty(host.Clipboard.Written);
        Assert.Contains(
            host.ChatLines,
            line => line.Contains("Clipboard is unavailable; nothing was copied.", StringComparison.Ordinal));
        Assert.DoesNotContain(
            host.ChatLines,
            line => line.Contains("has been copied to the clipboard", StringComparison.Ordinal));
        Assert.Contains(
            ((RecordingLogger)host.Log).Messages,
            message => message.Contains("Clipboard is unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public void CancelStopsWithoutPrintingCompletion()
    {
        (FakeHost host, _, _, InventoryExporter exporter) = Build();
        AddOwnedSword(host, 101u, identified: false);

        exporter.ExportToClipboard(ExportGroups.Inventory);
        host.Events.RaiseTick(0.1); // request pass; still waiting on ident

        exporter.Cancel();

        Assert.False(exporter.IsRunning);
        Assert.Contains(host.ChatLines, line => line.Contains("Copying"));
        Assert.DoesNotContain(host.ChatLines, line => line.Contains("has been copied"));
        Assert.Empty(host.Clipboard.Written);
    }

    [Fact]
    public void CancelWithNothingRunningIsANoOp()
    {
        (FakeHost host, _, _, InventoryExporter exporter) = Build();

        exporter.Cancel();

        Assert.False(exporter.IsRunning);
        Assert.Empty(host.ChatLines);
    }

    [Fact]
    public void SecondExportWhileRunningIsIgnored()
    {
        (FakeHost host, _, _, InventoryExporter exporter) = Build();
        AddOwnedSword(host, 101u, identified: true);

        exporter.ExportToClipboard(ExportGroups.Inventory);
        exporter.ExportToClipboard(ExportGroups.Inventory);

        Assert.Equal(1, host.ChatLines.Count(line => line.Contains("Copying")));
    }

    [Fact]
    public void AnItemThatNeverResolvesGivesUpAfterBoundedRetriesAndExportsWithoutIt()
    {
        // Defect 10 (live-gate round 3): Think() used to request ids once
        // and then, forever after, only ever set waitingForIdData=true for
        // anything still unappraised -- never re-requesting, never timing
        // out. Live: "Clipboard Inventory Info" printed "Copying..." and
        // never completed in 130+ seconds against one never-appraised item.
        (FakeHost host, _, _, InventoryExporter exporter) = Build();
        AddOwnedSword(host, 101u, identified: false);

        exporter.ExportToClipboard(ExportGroups.Inventory);

        // First think requests ids and marks the request pass done.
        host.Events.RaiseTick(0.1);
        Assert.Contains(101u, host.Automation.Objects.IdentifyRequests);

        // Drive well past the bounded retry budget (IdRetryInterval seconds
        // * (MaxIdRetries + 1)); ThinkInterval matches the 0.1s tick size,
        // so each RaiseTick drives exactly one Think call.
        for (int i = 0; i < 300 && host.Clipboard.Written.Count == 0; i++)
            host.Events.RaiseTick(0.1);

        Assert.False(exporter.IsRunning);
        Assert.Single(host.Clipboard.Written);
        Assert.Contains(
            host.ChatLines,
            line => line.Contains("never received identification data", StringComparison.Ordinal));
    }

    [Fact]
    public void AnItemThatResolvesLateIsIncludedInsteadOfBeingDropped()
    {
        // The bounded-retry fix for defect 10 must not drop an item that
        // simply takes a while to resolve -- only one that NEVER resolves
        // within the retry budget gives up.
        (FakeHost host, _, _, InventoryExporter exporter) = Build();
        AddOwnedSword(host, 101u, identified: false);

        exporter.ExportToClipboard(ExportGroups.Inventory);
        host.Events.RaiseTick(0.1); // request pass

        // Advance partway into the retry budget without ever identifying --
        // still short of the give-up threshold.
        for (int i = 0; i < 60; i++)
            host.Events.RaiseTick(0.1);

        Assert.Empty(host.Clipboard.Written);
        Assert.True(exporter.IsRunning);

        // The item resolves late.
        host.Automation.Objects.Replace(new PluginWorldObject(
            101u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 1u, 0u) { HasAppraisalData = true });
        host.Events.RaiseTick(0.1);

        Assert.Single(host.Clipboard.Written);
        Assert.Contains("Sword", host.Clipboard.Written[0]);
        Assert.DoesNotContain(
            host.ChatLines,
            line => line.Contains("never received identification data", StringComparison.Ordinal));
    }

    [Fact]
    public void GiveUpWithAnUnavailableClipboardOnlyPrintsTheClipboardMessage()
    {
        // LOW-8 (P10 review): the give-up branch printed "N item(s) never
        // received identification data..." unconditionally, then called
        // Stop(clipboardSet), which ALSO prints "Clipboard is unavailable;
        // nothing was copied." when the write failed -- two contradictory
        // messages for the same failed export (nothing was copied, so
        // reporting how many items were missing from a copy that never
        // happened is misleading).
        (FakeHost host, _, _, InventoryExporter exporter) = Build();
        AddOwnedSword(host, 101u, identified: false);
        host.Clipboard.Available = false;

        exporter.ExportToClipboard(ExportGroups.Inventory);
        host.Events.RaiseTick(0.1); // request pass

        for (int i = 0; i < 300 && exporter.IsRunning; i++)
            host.Events.RaiseTick(0.1);

        Assert.False(exporter.IsRunning);
        Assert.Contains(
            host.ChatLines,
            line => line.Contains("Clipboard is unavailable; nothing was copied.", StringComparison.Ordinal));
        Assert.DoesNotContain(
            host.ChatLines,
            line => line.Contains("never received identification data", StringComparison.Ordinal));
    }
}
