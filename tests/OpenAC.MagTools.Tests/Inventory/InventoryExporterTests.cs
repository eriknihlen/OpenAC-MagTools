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
        (FakeHost host, _, _, InventoryExporter exporter) = Build();
        host.Automation.Items.Owned.Add(FakeItems.Item(101u, "Equipped Sword", equippedLocation: 1u) with
        {
            ObjectClass = PluginObjectClass.MeleeWeapon,
            ContainerObjectId = host.Automation.Character.ObjectId,
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
}
