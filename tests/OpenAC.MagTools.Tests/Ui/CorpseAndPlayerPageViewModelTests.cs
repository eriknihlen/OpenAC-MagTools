using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Trackers.Corpse;
using OpenAC.MagTools.Trackers.Player;
using OpenAC.MagTools.Ui;

namespace OpenAC.MagTools.Tests.Ui;

public sealed class CorpseAndPlayerPageViewModelTests
{
    [Fact]
    public void CorpsePageProjectsTrackerRowsAndSelectingARowSelectsItsObject()
    {
        var host = new FakeHost();
        host.Automation.Character.Name = "Acdream";
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var corpseHost = new CorpseTrackerHost(host, settings.CorpseTracker);
        corpseHost.Tracker.ProcessWorldObject(
            new CorpseObservation(42u, 1, 10, 20, 0, "Corpse of Acdream", 0, false, null),
            "Acdream", false, _ => { });

        var vm = new CorpsePageViewModel(settings, corpseHost, host);

        Assert.Single(vm.Names);
        Assert.Equal("Corpse of Acdream", vm.Names[0]);

        vm.Select(0);

        Assert.Equal(42u, host.Selection.SelectedObjectId);
    }

    [Fact]
    public void CorpsePageClearHistoryEmptiesTheTracker()
    {
        var host = new FakeHost();
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var corpseHost = new CorpseTrackerHost(host, settings.CorpseTracker);
        corpseHost.Tracker.ProcessWorldObject(
            new CorpseObservation(42u, 1, 10, 20, 0, "Corpse of Acdream", 0, false, null),
            "Acdream", true, _ => { });

        var vm = new CorpsePageViewModel(settings, corpseHost, host);
        vm.ClearHistory();

        Assert.Empty(corpseHost.Tracker.Items);
        Assert.Empty(vm.Names);
    }

    [Fact]
    public void PlayerPageProjectsTrackerRowsAndSelectingARowSelectsItsObject()
    {
        var host = new FakeHost();
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var playerHost = new PlayerTrackerHost(host, settings.PlayerTracker);
        playerHost.Tracker.ProcessWorldObject(new PlayerObservation(7u, "Foo", 1, 0, 0, 0), "Acdream");

        var vm = new PlayerPageViewModel(settings, playerHost, host);

        Assert.Single(vm.Names);
        Assert.Equal("Foo", vm.Names[0]);

        vm.Select(0);

        Assert.Equal(7u, host.Selection.SelectedObjectId);
    }

    [Fact]
    public void PageViewModelsWithNoHostProjectEmptyListsAndToleraSelection()
    {
        var settings = new SettingsManager(new SettingsFile(new FakeHost().Storage));
        var corpseVm = new CorpsePageViewModel(settings);
        var playerVm = new PlayerPageViewModel(settings);

        Assert.Empty(corpseVm.Names);
        Assert.Empty(playerVm.Names);

        corpseVm.Select(0); // must not throw
        playerVm.Select(0);
    }
}
