using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Ui;
using Xunit;

namespace OpenAC.MagTools.Tests.Ui;

public sealed class ScopedCommandsPageViewModelTests
{
    private static (ScopedCommandsPageViewModel Vm, ScopedCommandStore Store, string Scope) Build()
    {
        var host = new FakeHost();
        var store = new ScopedCommandStore(new SettingsFile(host.Storage));
        var vm = new ScopedCommandsPageViewModel(store);
        string scope = SettingsScope.Character("acct", "Server", "Acdream");
        vm.Bind(scope);
        return (vm, store, scope);
    }

    [Fact]
    public void UnboundViewModelIsInertAndEmpty()
    {
        var vm = new ScopedCommandsPageViewModel();

        vm.SetLoginText("/mt test");
        vm.AddLoginCommand();

        Assert.Empty(vm.LoginCommands);
    }

    [Fact]
    public void AddLoginCommandAppendsAndClearsTheTextBoxAndPersists()
    {
        (ScopedCommandsPageViewModel vm, ScopedCommandStore store, string scope) = Build();

        vm.SetLoginText("  /mt autopack  ");
        vm.AddLoginCommand();

        Assert.Equal(["/mt autopack"], vm.LoginCommands);
        Assert.Equal(string.Empty, vm.LoginText);
        Assert.Equal(["/mt autopack"], store.GetOnLoginCommands(scope));
    }

    [Fact]
    public void AddLoginCommandIgnoresBlankText()
    {
        (ScopedCommandsPageViewModel vm, _, _) = Build();

        vm.SetLoginText("   ");
        vm.AddLoginCommand();

        Assert.Empty(vm.LoginCommands);
    }

    [Fact]
    public void MoveLoginCommandUpSwapsAndPersists()
    {
        (ScopedCommandsPageViewModel vm, ScopedCommandStore store, string scope) = Build();
        vm.SetLoginText("first");
        vm.AddLoginCommand();
        vm.SetLoginText("second");
        vm.AddLoginCommand();

        vm.MoveLoginCommandUp(1);

        Assert.Equal(["second", "first"], vm.LoginCommands);
        Assert.Equal(["second", "first"], store.GetOnLoginCommands(scope));
        Assert.Equal(0, vm.LoginSelectedRow);
    }

    [Fact]
    public void MoveLoginCommandUpAtTheTopIsANoOp()
    {
        (ScopedCommandsPageViewModel vm, _, _) = Build();
        vm.SetLoginText("only");
        vm.AddLoginCommand();

        vm.MoveLoginCommandUp(0);

        Assert.Equal(["only"], vm.LoginCommands);
    }

    [Fact]
    public void DeleteLoginCommandRemovesAndPersists()
    {
        (ScopedCommandsPageViewModel vm, ScopedCommandStore store, string scope) = Build();
        vm.SetLoginText("a");
        vm.AddLoginCommand();
        vm.SetLoginText("b");
        vm.AddLoginCommand();

        vm.DeleteLoginCommand(0);

        Assert.Equal(["b"], vm.LoginCommands);
        Assert.Equal(["b"], store.GetOnLoginCommands(scope));
    }

    [Fact]
    public void DeletingTheSelectedRowClearsSelection()
    {
        (ScopedCommandsPageViewModel vm, _, _) = Build();
        vm.SetLoginText("a");
        vm.AddLoginCommand();
        vm.SelectLoginRow(0);

        vm.DeleteLoginCommand(0);

        Assert.Equal(-1, vm.LoginSelectedRow);
    }

    [Fact]
    public void DeletingBeforeTheSelectedRowShiftsSelectionDown()
    {
        (ScopedCommandsPageViewModel vm, _, _) = Build();
        vm.SetLoginText("a");
        vm.AddLoginCommand();
        vm.SetLoginText("b");
        vm.AddLoginCommand();
        vm.SelectLoginRow(1);

        vm.DeleteLoginCommand(0);

        Assert.Equal(0, vm.LoginSelectedRow);
    }

    [Fact]
    public void LoginCompleteListBehavesTheSameAsLogin()
    {
        (ScopedCommandsPageViewModel vm, ScopedCommandStore store, string scope) = Build();
        vm.SetLoginCompleteText("/mt opt list");
        vm.AddLoginCompleteCommand();

        Assert.Equal(["/mt opt list"], vm.LoginCompleteCommands);
        Assert.Equal(["/mt opt list"], store.GetOnLoginCompleteCommands(scope));

        vm.DeleteLoginCompleteCommand(0);
        Assert.Empty(vm.LoginCompleteCommands);
        Assert.Empty(store.GetOnLoginCompleteCommands(scope));
    }

    [Fact]
    public void AddPeriodicCommandParsesIntervalAndOffsetAndResetsTheFields()
    {
        (ScopedCommandsPageViewModel vm, ScopedCommandStore store, string scope) = Build();

        vm.SetPeriodicText("/mt autopack");
        vm.SetPeriodicInterval("5");
        vm.SetPeriodicOffset("2");
        vm.AddPeriodicCommand();

        Assert.Equal(["/mt autopack"], vm.PeriodicCommands);
        Assert.Equal(["5"], vm.PeriodicIntervals);
        Assert.Equal(["2"], vm.PeriodicOffsets);
        Assert.Equal("1", vm.PeriodicIntervalText);
        Assert.Equal("0", vm.PeriodicOffsetText);
        Assert.Equal(string.Empty, vm.PeriodicText);

        IReadOnlyList<PeriodicCommand> stored = store.GetPeriodicCommands(scope);
        PeriodicCommand only = Assert.Single(stored);
        Assert.Equal("/mt autopack", only.Command);
        Assert.Equal(TimeSpan.FromMinutes(5), only.Interval);
        Assert.Equal(TimeSpan.FromMinutes(2), only.OffsetFromMidnight);
    }

    [Fact]
    public void AddPeriodicCommandFallsBackToDefaultsOnUnparsableIntervalOrOffset()
    {
        (ScopedCommandsPageViewModel vm, ScopedCommandStore store, string scope) = Build();

        vm.SetPeriodicText("/mt autopack");
        vm.SetPeriodicInterval("not a number");
        vm.SetPeriodicOffset("also not a number");
        vm.AddPeriodicCommand();

        PeriodicCommand only = Assert.Single(store.GetPeriodicCommands(scope));
        Assert.Equal(TimeSpan.FromMinutes(1), only.Interval);
        Assert.Equal(TimeSpan.Zero, only.OffsetFromMidnight);
    }

    [Fact]
    public void DeletePeriodicCommandRemovesAndPersists()
    {
        (ScopedCommandsPageViewModel vm, ScopedCommandStore store, string scope) = Build();
        vm.SetPeriodicText("a");
        vm.AddPeriodicCommand();
        vm.SetPeriodicText("b");
        vm.AddPeriodicCommand();

        vm.DeletePeriodicCommand(0);

        Assert.Equal(["b"], vm.PeriodicCommands);
        Assert.Equal(["b"], store.GetPeriodicCommands(scope).Select(c => c.Command));
    }

    [Fact]
    public void BindReloadsFromStorageForTheNewScope()
    {
        var host = new FakeHost();
        var store = new ScopedCommandStore(new SettingsFile(host.Storage));
        string characterScope = SettingsScope.Character("acct", "Server", "Acdream");
        store.SetOnLoginCommands(characterScope, ["already saved"]);

        var vm = new ScopedCommandsPageViewModel(store);
        vm.Bind(characterScope);

        Assert.Equal(["already saved"], vm.LoginCommands);
    }

    [Fact]
    public void RowIconsRepeatOneEntryPerRow()
    {
        (ScopedCommandsPageViewModel vm, _, _) = Build();
        vm.SetLoginText("a");
        vm.AddLoginCommand();
        vm.SetLoginText("b");
        vm.AddLoginCommand();

        Assert.Equal(
            [ScopedCommandsPageViewModel.UpIconId, ScopedCommandsPageViewModel.UpIconId],
            vm.LoginUpIcons);
        Assert.Equal(
            [ScopedCommandsPageViewModel.DeleteIconId, ScopedCommandsPageViewModel.DeleteIconId],
            vm.LoginDeleteIcons);
    }
}
