using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using PlayerTrackerHost = OpenAC.MagTools.Trackers.Player.PlayerTrackerHost;

namespace OpenAC.MagTools.Tests.Trackers.Player;

public sealed class PlayerTrackerHostTests
{
    private static (FakeHost Host, PlayerTrackerHost TrackerHost, OpenAC.MagTools.TickScheduler Scheduler) Make()
    {
        var host = new FakeHost();
        host.Automation.Character.Name = "Acdream";
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        settings.PlayerTracker.Enabled.Value = true;
        settings.PlayerTracker.Persistent.Value = true;
        var chat = new ChatOutput(host);
        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, chat);
        var trackerHost = new PlayerTrackerHost(host, settings.PlayerTracker);
        trackerHost.Start(scheduler, "ACServer", "Acdream");
        return (host, trackerHost, scheduler);
    }

    private static PluginNavigationPosition CompassPosition(uint cellId, double localX, double localY)
    {
        uint blockX = (cellId >> 24) & 0xFFu;
        uint blockY = (cellId >> 16) & 0xFFu;
        double ew = ((blockX - 127d) * 192d + localX - 84d) / 240d;
        double ns = ((blockY - 127d) * 192d + localY - 84d) / 240d;
        return new PluginNavigationPosition(cellId, ew, ns, 0d, 0f, true);
    }

    [Fact]
    public void CreatedPlayerStoresTheFullCellIdAndLandblockLocalMetres()
    {
        (FakeHost host, PlayerTrackerHost trackerHost, _) = Make();
        const uint cellId = 0x7D64001Fu;
        host.Automation.Objects.Objects.Add(new PluginWorldObject(9u, 0u, "Foo", PluginObjectClass.Player, 0u, 0u, 0u)
        {
            Position = CompassPosition(cellId, 100d, 50d),
        });

        host.Events.RaiseObjectChanged(9u, PluginObjectChangeKind.Created);

        var tracked = Assert.Single(trackerHost.Tracker.Items);
        Assert.Equal(unchecked((int)cellId), tracked.LandBlock);
        Assert.Equal(100d, tracked.LocationX, precision: 6);
        Assert.Equal(50d, tracked.LocationY, precision: 6);
    }

    [Fact]
    public void StopPersistsTheTrackedPlayers()
    {
        (FakeHost host, PlayerTrackerHost trackerHost, _) = Make();
        host.Automation.Objects.Objects.Add(new PluginWorldObject(9u, 0u, "Foo", PluginObjectClass.Player, 0u, 0u, 0u)
        {
            Position = CompassPosition(0x7D64001Fu, 100d, 50d),
        });
        host.Events.RaiseObjectChanged(9u, PluginObjectChangeKind.Created);

        trackerHost.Stop();

        string? saved = host.Storage.ReadText("ACServer/Acdream.PlayerTracker.xml");
        Assert.NotNull(saved);
        Assert.Contains("Foo", saved);
    }
}
