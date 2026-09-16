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
    public void PlayerPageSelectsTheRowActuallyDisplayedNotAFreshlyResortedOne()
    {
        // M5: PlayerTrackerRowsBuilder rate-limits its full re-sort to once
        // per 10 seconds. A fresh builder constructed just for a click
        // always looks like "the very first call ever" (no prior _lastSort)
        // and sorts immediately -- which can disagree with what the shared,
        // already-primed display builder is currently showing within its
        // rate-limit window.
        var clock = new DateTime(2026, 9, 16, 12, 0, 0);
        var host = new FakeHost();
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var playerHost = new PlayerTrackerHost(host, settings.PlayerTracker);
        var vm = new PlayerPageViewModel(settings, playerHost, host);

        // Reach into the tracker with an injected clock via a second tracker
        // sharing the same host would be awkward -- instead drive the
        // PlayerTracker directly (its own Func<DateTime> defaults to
        // DateTime.Now, which we can't control), so this test uses the
        // tracker's real clock but controls ORDER via explicit imports that
        // pin exact LastSeen values, then reads the view model's cached
        // builder across two ticks.
        playerHost.Tracker.ImportStats(
        [
            new OpenAC.MagTools.Trackers.Player.TrackedPlayer("Bob", clock, 1, 0, 0, 0, 100u),
        ]);
        playerHost.Tracker.ImportStats(
        [
            new OpenAC.MagTools.Trackers.Player.TrackedPlayer("Alice", clock.AddSeconds(1), 1, 0, 0, 0, 200u),
        ]);

        // Prime the shared builder (its first call always sorts -- this
        // establishes the rate-limit window from here).
        _ = vm.Names;

        // Bob is re-seen with a LATER LastSeen than Alice, but since he's
        // already tracked, no re-insertion happens -- display order (within
        // the 10s window) stays whatever it already was; only a fresh
        // eager-sort would put Bob back on top from this alone.
        playerHost.Tracker.ProcessWorldObject(new PlayerObservation(100u, "Bob", 1, 0, 0, 0), "Acdream");

        var displayed = vm.Names;
        int topRow = 0;
        string expectedName = displayed[topRow];
        uint expectedId = expectedName == "Bob" ? 100u : 200u;

        vm.Select(topRow);

        Assert.Equal(expectedId, host.Selection.SelectedObjectId);
    }

    [Fact]
    public void CorpsePageCachesRowsUntilTheTrackerReportsAChange()
    {
        var host = new FakeHost();
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var corpseHost = new CorpseTrackerHost(host, settings.CorpseTracker);
        corpseHost.Tracker.ProcessWorldObject(
            new CorpseObservation(1u, 1, 10, 20, 0, "Corpse of Acdream", 0, false, null),
            "Acdream", false, _ => { });

        var vm = new CorpsePageViewModel(settings, corpseHost, host);

        IReadOnlyList<string> first = vm.Names;
        IReadOnlyList<string> second = vm.Names;
        Assert.Same(first, second); // no rebuild -- same cached list instance

        corpseHost.Tracker.ProcessWorldObject(
            new CorpseObservation(2u, 1, 10, 20, 0, "Corpse of a Drudge", 0, false, null),
            "Acdream", true, _ => { });

        IReadOnlyList<string> third = vm.Names;
        Assert.NotSame(second, third);
        Assert.Equal(2, third.Count);
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
