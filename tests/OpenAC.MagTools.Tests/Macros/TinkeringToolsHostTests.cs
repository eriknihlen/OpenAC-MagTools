using AcDream.Plugin.Abstractions;
using OpenAC.MagTools;
using OpenAC.MagTools.ItemInfo;
using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Tests.Fakes;

namespace OpenAC.MagTools.Tests.Macros;

public sealed class TinkeringToolsHostTests
{
    private static (FakeHost Host, TinkeringToolsHost ToolsHost, TickScheduler Scheduler) Make()
    {
        var host = new FakeHost();
        host.Automation.Combat.Snapshot = new PluginCombatSnapshot(0u, PluginCombatMode.Peace, default, 0f, 0f, false, false, false, false);
        var chat = new ChatOutput(host);
        var scheduler = new TickScheduler(host.Events, chat);
        var toolsHost = new TinkeringToolsHost(host);
        toolsHost.Attach(scheduler);
        return (host, toolsHost, scheduler);
    }

    [Theory]
    [InlineData(PluginObjectClass.Armor, true)]
    [InlineData(PluginObjectClass.Clothing, true)]
    [InlineData(PluginObjectClass.MeleeWeapon, true)]
    [InlineData(PluginObjectClass.MissileWeapon, true)]
    [InlineData(PluginObjectClass.WandStaffOrb, true)]
    [InlineData(PluginObjectClass.Jewelry, true)]
    [InlineData(PluginObjectClass.Food, false)]
    [InlineData(PluginObjectClass.Misc, false)]
    public void CanAddMatchesTheOriginalsClassFilter(PluginObjectClass objectClass, bool expected)
        => Assert.Equal(expected, TinkeringToolsHost.CanAdd(objectClass));

    [Fact]
    public void AddSelectedItemAddsARowAndRequestsAnId()
    {
        (FakeHost host, TinkeringToolsHost toolsHost, _) = Make();
        host.Automation.Objects.Objects.Add(new PluginWorldObject(1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u) { IconId = 42u });
        host.Selection.Select(1u);

        toolsHost.AddSelectedItem();

        var row = Assert.Single(toolsHost.Rows);
        Assert.Equal(1u, row.ObjectId);
        Assert.Equal(42u, row.IconId);
        Assert.Equal("Sword", row.Name);
        Assert.Equal(string.Empty, row.Work);
        Assert.Equal(string.Empty, row.Tinks);
        Assert.Contains(1u, host.Automation.Objects.IdentifyRequests);
    }

    [Fact]
    public void AddSelectedItemIgnoresAnUnsupportedClass()
    {
        (FakeHost host, TinkeringToolsHost toolsHost, _) = Make();
        host.Automation.Objects.Objects.Add(new PluginWorldObject(1u, 0u, "Trinket", PluginObjectClass.Misc, 0u, 500u, 0u));
        host.Selection.Select(1u);

        toolsHost.AddSelectedItem();

        Assert.Empty(toolsHost.Rows);
    }

    [Fact]
    public void AddSelectedItemDoesNotAddTheSameItemTwice()
    {
        (FakeHost host, TinkeringToolsHost toolsHost, _) = Make();
        host.Automation.Objects.Objects.Add(new PluginWorldObject(1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u));
        host.Selection.Select(1u);

        toolsHost.AddSelectedItem();
        toolsHost.AddSelectedItem();

        Assert.Single(toolsHost.Rows);
    }

    [Fact]
    public void IdentReceivedFillsWorkAndTinksForATrackedRow()
    {
        (FakeHost host, TinkeringToolsHost toolsHost, _) = Make();
        host.Automation.Objects.Objects.Add(new PluginWorldObject(1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u));
        host.Selection.Select(1u);
        toolsHost.AddSelectedItem();

        host.Automation.Objects.Properties[1u] = new PluginItemProperties(
            new Dictionary<uint, int> { { (uint)ItemModel.WorkmanshipKey, 7 }, { (uint)ItemModel.NumberTimesTinkeredKey, 3 } },
            new Dictionary<uint, long>(), new Dictionary<uint, bool>(), new Dictionary<uint, double>(),
            new Dictionary<uint, string>(), new Dictionary<uint, uint>(), new Dictionary<uint, uint>());
        host.Events.RaiseObjectChanged(1u, PluginObjectChangeKind.IdentReceived);

        var row = Assert.Single(toolsHost.Rows);
        Assert.Equal("7", row.Work);
        Assert.Equal("3", row.Tinks);
    }

    [Fact]
    public void DeleteRowRemovesIt()
    {
        (FakeHost host, TinkeringToolsHost toolsHost, _) = Make();
        host.Automation.Objects.Objects.Add(new PluginWorldObject(1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u));
        host.Selection.Select(1u);
        toolsHost.AddSelectedItem();

        toolsHost.DeleteRow(0);

        Assert.Empty(toolsHost.Rows);
    }

    [Fact]
    public void SelectRowSelectsTheRowsObject()
    {
        (FakeHost host, TinkeringToolsHost toolsHost, _) = Make();
        host.Automation.Objects.Objects.Add(new PluginWorldObject(7u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u));
        host.Selection.Select(7u);
        toolsHost.AddSelectedItem();

        host.Selection.Select(0u); // clear
        toolsHost.SelectRow(0);

        Assert.Equal(7u, host.Selection.SelectedObjectId);
    }

    private static void AddSalvage(FakeHost host, uint objectId, string material, bool identified = false)
    {
        host.Automation.Items.Owned.Add(new PluginInventoryItem(
            objectId, 0u, "Bag of Salvage", 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0) { ObjectClass = PluginObjectClass.Salvage });
        host.Automation.Objects.Objects.Add(new PluginWorldObject(objectId, 0u, "Bag of Salvage", PluginObjectClass.Salvage, 0u, 500u, 0u)
        {
            HasAppraisalData = identified,
        });
        host.Automation.Objects.Properties[objectId] = new PluginItemProperties(
            new Dictionary<uint, int> { { (uint)ItemModel.MaterialKey, MaterialIdFor(material) } },
            new Dictionary<uint, long>(), new Dictionary<uint, bool>(), new Dictionary<uint, double>(),
            new Dictionary<uint, string>(), new Dictionary<uint, uint>(), new Dictionary<uint, uint>());
    }

    private static int MaterialIdFor(string name)
        => Dictionaries.MaterialInfo.First(kvp => kvp.Value == name).Key;

    [Fact]
    public void StartScanRequestsIdsForEveryUnidentifiedMatchingSalvageBagImmediately()
    {
        (FakeHost host, TinkeringToolsHost toolsHost, _) = Make();
        AddSalvage(host, 1u, "Iron");
        AddSalvage(host, 2u, "Iron");
        AddSalvage(host, 3u, "Steel"); // wrong material

        toolsHost.StartScan(() => "Iron");

        Assert.Contains(1u, host.Automation.Objects.IdentifyRequests);
        Assert.Contains(2u, host.Automation.Objects.IdentifyRequests);
        Assert.DoesNotContain(3u, host.Automation.Objects.IdentifyRequests);
    }

    [Fact]
    public void ScanTickRequestsOneAtATimeAndStopsWhenNoneRemain()
    {
        (FakeHost host, TinkeringToolsHost toolsHost, TickScheduler scheduler) = Make();
        AddSalvage(host, 1u, "Iron");

        toolsHost.StartScan(() => "Iron");
        host.Automation.Objects.IdentifyRequests.Clear(); // clear the initial burst request

        // The bag never actually becomes identified in this test, so a tick
        // should re-request it exactly once per second, not stop.
        scheduler.Tick(1.0);
        Assert.Equal([1u], host.Automation.Objects.IdentifyRequests);

        // Now nothing left to match: mark it identified.
        host.Automation.Objects.Objects[0] = host.Automation.Objects.Objects[0] with { HasAppraisalData = true };
        host.Automation.Objects.IdentifyRequests.Clear();
        scheduler.Tick(1.0);

        Assert.Empty(host.Automation.Objects.IdentifyRequests);
        Assert.False(toolsHost.IsScanning);
    }

    [Fact]
    public void ScanStopsWhenCombatModeLeavesPeace()
    {
        (FakeHost host, TinkeringToolsHost toolsHost, TickScheduler scheduler) = Make();
        AddSalvage(host, 1u, "Iron");
        toolsHost.StartScan(() => "Iron");

        host.Automation.Combat.Snapshot = new PluginCombatSnapshot(0u, PluginCombatMode.Melee, default, 0f, 0f, false, false, false, false);
        scheduler.Tick(1.0);

        Assert.False(toolsHost.IsScanning);
    }

    [Fact]
    public void ScanSkipsATickWhileBusyButStaysRunning()
    {
        (FakeHost host, TinkeringToolsHost toolsHost, TickScheduler scheduler) = Make();
        AddSalvage(host, 1u, "Iron");
        toolsHost.StartScan(() => "Iron");
        host.Automation.Items.IsBusy = true;
        host.Automation.Objects.IdentifyRequests.Clear();

        scheduler.Tick(1.0);

        Assert.Empty(host.Automation.Objects.IdentifyRequests);
        Assert.True(toolsHost.IsScanning);
    }

    [Fact]
    public void StopScanStopsTheTimer()
    {
        (FakeHost host, TinkeringToolsHost toolsHost, TickScheduler scheduler) = Make();
        AddSalvage(host, 1u, "Iron");
        toolsHost.StartScan(() => "Iron");
        host.Automation.Objects.IdentifyRequests.Clear();

        toolsHost.StopScan();
        scheduler.Tick(1.0);

        Assert.Empty(host.Automation.Objects.IdentifyRequests);
        Assert.False(toolsHost.IsScanning);
    }

    [Fact]
    public void ScanTickReReadsTheMaterialProviderRatherThanTheValueCapturedAtStart()
    {
        // The original re-reads the dropdown's CURRENT selection on every
        // timer tick, not just once when Start was clicked -- a material
        // switch mid-scan takes effect on the very next tick.
        (FakeHost host, TinkeringToolsHost toolsHost, TickScheduler scheduler) = Make();
        AddSalvage(host, 1u, "Iron");
        AddSalvage(host, 2u, "Steel");

        string selected = "Iron";
        toolsHost.StartScan(() => selected);
        host.Automation.Objects.IdentifyRequests.Clear();

        // Switch the live selection AFTER Start captured the provider, not the value.
        selected = "Steel";
        scheduler.Tick(1.0);

        Assert.Contains(2u, host.Automation.Objects.IdentifyRequests);
        Assert.DoesNotContain(1u, host.Automation.Objects.IdentifyRequests);
    }

    [Fact]
    public void IdentReceivedDefaultsWorkAndTinksToMinusOneWhenThePropertiesAreAbsent()
    {
        // -1 (not 0) matches the original's MyWorldObject property-lookup
        // sentinel for "not present" -- 0 is a real, distinct value.
        (FakeHost host, TinkeringToolsHost toolsHost, _) = Make();
        host.Automation.Objects.Objects.Add(new PluginWorldObject(1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u));
        host.Selection.Select(1u);
        toolsHost.AddSelectedItem();

        host.Automation.Objects.Properties[1u] = new PluginItemProperties(
            new Dictionary<uint, int>(),
            new Dictionary<uint, long>(), new Dictionary<uint, bool>(), new Dictionary<uint, double>(),
            new Dictionary<uint, string>(), new Dictionary<uint, uint>(), new Dictionary<uint, uint>());
        host.Events.RaiseObjectChanged(1u, PluginObjectChangeKind.IdentReceived);

        var row = Assert.Single(toolsHost.Rows);
        Assert.Equal("-1", row.Work);
        Assert.Equal("-1", row.Tinks);
    }
}
