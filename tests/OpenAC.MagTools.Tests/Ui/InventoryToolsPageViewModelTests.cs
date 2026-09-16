using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Tests.ItemInfo;
using OpenAC.MagTools.Ui;

namespace OpenAC.MagTools.Tests.Ui;

public sealed class InventoryToolsPageViewModelTests
{
    private static (FakeHost Host, SettingsManager Settings, InventoryToolsPageViewModel Vm) Build()
    {
        var host = new FakeHost();
        host.Automation.Character.ObjectId = 1u;
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var vm = new InventoryToolsPageViewModel(host, settings.ItemInfoOnIdent, new FakeFormatterSpellCatalog());
        return (host, settings, vm);
    }

    private static void AddItem(FakeHost host, uint id, string name, uint containerId)
    {
        host.Automation.Items.Owned.Add(FakeItems.Item(id, name) with { ContainerObjectId = containerId });
        host.Automation.Objects.Replace(new PluginWorldObject(
            id, 0u, name, PluginObjectClass.Misc, 0u, containerId, 0u) { HasAppraisalData = true });
    }

    [Fact]
    public void SearchFiltersOwnedItemsByFormattedInfoLine()
    {
        (FakeHost host, _, InventoryToolsPageViewModel vm) = Build();
        AddItem(host, 101u, "Sword", 1u);
        AddItem(host, 102u, "Shield", 1u);

        vm.SetSearchText("Sword");

        Assert.Single(vm.ResultNames);
        Assert.Equal("Sword", vm.ResultNames[0]);
    }

    [Fact]
    public void SearchIsCaseInsensitive()
    {
        (FakeHost host, _, InventoryToolsPageViewModel vm) = Build();
        AddItem(host, 101u, "Sword", 1u);

        vm.SetSearchText("sWoRd");

        Assert.Single(vm.ResultNames);
    }

    [Fact]
    public void InvalidRegexProducesAnEmptyResultListInsteadOfThrowing()
    {
        (FakeHost host, _, InventoryToolsPageViewModel vm) = Build();
        AddItem(host, 101u, "Sword", 1u);

        var exception = Record.Exception(() => vm.SetSearchText("[unterminated"));

        Assert.Null(exception);
        Assert.Empty(vm.ResultNames);
    }

    [Fact]
    public void ClickingAResultInMainPackDoesNotUseTheContainer()
    {
        (FakeHost host, _, InventoryToolsPageViewModel vm) = Build();
        AddItem(host, 101u, "Sword", 1u); // container == character id (main pack)
        vm.SetSearchText("Sword");

        vm.SelectResultRow(0);

        Assert.Empty(host.Automation.Items.Calls);
        Assert.Equal(101u, host.Selection.SelectedObjectId);
        Assert.Single(vm.ItemInfoLines);
        Assert.Contains("Sword", vm.ItemInfoLines[0]);
    }

    [Fact]
    public void ClickingAResultInASidePackOpensItsContainer()
    {
        (FakeHost host, _, InventoryToolsPageViewModel vm) = Build();
        AddItem(host, 101u, "Sword", 50u); // side pack
        vm.SetSearchText("Sword");

        vm.SelectResultRow(0);

        Assert.Contains(("use", 50u, 0u), host.Automation.Items.Calls);
    }

    [Fact]
    public void ClickingAnEquippedResultDoesNotUseTheContainer()
    {
        // MEDIUM-6 (P10 review): on this host an equipped item's
        // ContainerObjectId is whatever container it was equipped FROM (or
        // 0), never the player -- the same host-shape fact behind defect
        // 9's IsEquippedByMe fix -- so SelectRow's plain
        // "ContainerObjectId != Character.ObjectId" check treated every
        // equipped item as being in a foreign container and called
        // Items.Use(0) for it. An equipped item (WielderObjectId == me)
        // must never trigger a container-open.
        (FakeHost host, _, InventoryToolsPageViewModel vm) = Build();
        host.Automation.Items.Owned.Add(FakeItems.Item(101u, "Equipped Sword", equippedLocation: 1u) with
        {
            ContainerObjectId = 0u,
            WielderObjectId = host.Automation.Character.ObjectId,
        });
        host.Automation.Objects.Replace(new PluginWorldObject(
            101u, 0u, "Equipped Sword", PluginObjectClass.Misc, 0u, 0u, host.Automation.Character.ObjectId)
        { HasAppraisalData = true });
        vm.SetSearchText("Equipped Sword");

        vm.SelectResultRow(0);

        Assert.Empty(host.Automation.Items.Calls);
        Assert.Equal(101u, host.Selection.SelectedObjectId);
    }

    [Fact]
    public void OutOfRangeSelectionIsIgnored()
    {
        (FakeHost host, _, InventoryToolsPageViewModel vm) = Build();
        AddItem(host, 101u, "Sword", 1u);
        vm.SetSearchText("Sword");

        var exception = Record.Exception(() => vm.SelectResultRow(5));

        Assert.Null(exception);
        Assert.Empty(vm.ItemInfoLines);
    }
}
