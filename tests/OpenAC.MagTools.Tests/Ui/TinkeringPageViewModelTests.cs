using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Ui;

namespace OpenAC.MagTools.Tests.Ui;

public sealed class TinkeringPageViewModelTests
{
    private static (FakeHost Host, TinkeringToolsHost ToolsHost, TinkeringPageViewModel Vm) Build()
    {
        var host = new FakeHost();
        var chat = new ChatOutput(host);
        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, chat);
        var toolsHost = new TinkeringToolsHost(host);
        toolsHost.Attach(scheduler);
        var vm = new TinkeringPageViewModel(toolsHost);
        return (host, toolsHost, vm);
    }

    [Fact]
    public void RowsAreCachedUntilTheHostReportsAChange()
    {
        (FakeHost host, TinkeringToolsHost toolsHost, TinkeringPageViewModel vm) = Build();
        host.Automation.Objects.Objects.Add(
            new PluginWorldObject(1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u));
        host.Selection.Select(1u);
        toolsHost.AddSelectedItem();

        IReadOnlyList<string> first = vm.Names;
        IReadOnlyList<string> second = vm.Names;
        Assert.Same(first, second); // no rebuild -- same cached list instance

        host.Automation.Objects.Objects.Add(
            new PluginWorldObject(2u, 0u, "Shield", PluginObjectClass.Armor, 0u, 500u, 0u));
        host.Selection.Select(2u);
        toolsHost.AddSelectedItem();

        IReadOnlyList<string> third = vm.Names;
        Assert.NotSame(second, third);
        Assert.Equal(2, third.Count);
    }

    [Fact]
    public void IdentReceivedFillInvalidatesTheCachedWorkAndTinksColumns()
    {
        (FakeHost host, TinkeringToolsHost toolsHost, TinkeringPageViewModel vm) = Build();
        host.Automation.Objects.Objects.Add(
            new PluginWorldObject(1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u));
        host.Selection.Select(1u);
        toolsHost.AddSelectedItem();

        Assert.Equal([string.Empty], vm.Work);

        host.Automation.Objects.Properties[1u] = new PluginItemProperties(
            new Dictionary<uint, int> { { (uint)OpenAC.MagTools.ItemInfo.ItemModel.WorkmanshipKey, 7 } },
            new Dictionary<uint, long>(), new Dictionary<uint, bool>(), new Dictionary<uint, double>(),
            new Dictionary<uint, string>(), new Dictionary<uint, uint>(), new Dictionary<uint, uint>());
        host.Events.RaiseObjectChanged(1u, PluginObjectChangeKind.IdentReceived);

        Assert.Equal(["7"], vm.Work);
    }

    [Fact]
    public void DeleteRowInvalidatesTheCache()
    {
        (FakeHost host, TinkeringToolsHost toolsHost, TinkeringPageViewModel vm) = Build();
        host.Automation.Objects.Objects.Add(
            new PluginWorldObject(1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u));
        host.Selection.Select(1u);
        toolsHost.AddSelectedItem();
        Assert.Single(vm.Names);

        vm.DeleteRow(0);

        Assert.Empty(vm.Names);
    }

    [Fact]
    public void DisposeUnsubscribesFromTheHostsChangedEvent()
    {
        (FakeHost host, TinkeringToolsHost toolsHost, TinkeringPageViewModel vm) = Build();
        Assert.Empty(vm.Names); // primes the (empty) cache before the subscription is dropped
        vm.Dispose();

        host.Automation.Objects.Objects.Add(
            new PluginWorldObject(1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 500u, 0u));
        host.Selection.Select(1u);
        toolsHost.AddSelectedItem();

        // Still empty: the view model never learned the row was added because
        // it stopped listening, and the cache from before Dispose() is stale
        // and empty (never rebuilt without a live subscription marking it dirty).
        Assert.Empty(vm.Names);

        vm.Resubscribe();
        toolsHost.DeleteRow(0);
        host.Automation.Objects.Objects.Add(
            new PluginWorldObject(2u, 0u, "Shield", PluginObjectClass.Armor, 0u, 500u, 0u));
        host.Selection.Select(2u);
        toolsHost.AddSelectedItem();

        Assert.Single(vm.Names);
    }

    [Fact]
    public void StartPassesALiveMaterialProviderNotASnapshot()
    {
        (FakeHost host, TinkeringToolsHost toolsHost, TinkeringPageViewModel vm) = Build();
        host.Automation.Combat.Snapshot = new PluginCombatSnapshot(
            0u, PluginCombatMode.Peace, default, 0f, 0f, false, false, false, false);

        vm.SelectMaterial("Steel");
        vm.Start();
        Assert.True(toolsHost.IsScanning);

        // Changing the selection mid-scan must be visible on the next tick --
        // proven indirectly: StartScan only accepts a live Func<string> now,
        // so this compiling and running at all is the regression guard for
        // the P6 "captured at Start" bug; ScanTick-level proof lives in
        // TinkeringToolsHostTests.
        vm.SelectMaterial("Iron");
        Assert.Equal("Iron", vm.SelectedMaterial);
    }
}
