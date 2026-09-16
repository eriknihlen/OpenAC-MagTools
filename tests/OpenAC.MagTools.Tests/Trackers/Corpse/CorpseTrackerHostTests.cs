using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using CorpseTrackerHost = OpenAC.MagTools.Trackers.Corpse.CorpseTrackerHost;

namespace OpenAC.MagTools.Tests.Trackers.Corpse;

public sealed class CorpseTrackerHostTests
{
    private static (FakeHost Host, CorpseTrackerHost TrackerHost, OpenAC.MagTools.TickScheduler Scheduler) Make(
        bool trackAllCorpses = true)
    {
        var host = new FakeHost();
        host.Automation.Character.Name = "Acdream";
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        settings.CorpseTracker.TrackAllCorpses.Value = trackAllCorpses;
        var chat = new ChatOutput(host);
        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, chat);
        var trackerHost = new CorpseTrackerHost(host, settings.CorpseTracker);
        trackerHost.Start(scheduler, "ACServer", "Acdream");
        return (host, trackerHost, scheduler);
    }

    /// <summary>Builds a live host position for a cell, given landblock-local metres (matching wo.RawCoordinates()).</summary>
    private static PluginNavigationPosition CompassPosition(uint cellId, double localX, double localY, double localZ = 0d)
    {
        uint blockX = (cellId >> 24) & 0xFFu;
        uint blockY = (cellId >> 16) & 0xFFu;
        double ew = ((blockX - 127d) * 192d + localX - 84d) / 240d;
        double ns = ((blockY - 127d) * 192d + localY - 84d) / 240d;
        return new PluginNavigationPosition(cellId, ew, ns, localZ / 240d, 0f, true);
    }

    [Fact]
    public void CreatedCorpseStoresTheFullCellIdAndLandblockLocalMetres()
    {
        (FakeHost host, var trackerHost, _) = Make();
        const uint cellId = 0x7D64001Fu;
        host.Automation.Objects.Objects.Add(new PluginWorldObject(1u, 0u, "Corpse of a Drudge", PluginObjectClass.Corpse, 0u, 0u, 0u)
        {
            HasAppraisalData = false,
            Position = CompassPosition(cellId, 100d, 50d),
        });

        host.Events.RaiseObjectChanged(1u, PluginObjectChangeKind.Created);

        var tracked = Assert.Single(trackerHost.Tracker.Items);
        Assert.Equal(unchecked((int)cellId), tracked.LandBlock);
        Assert.Equal(100d, tracked.LocationX, precision: 6);
        Assert.Equal(50d, tracked.LocationY, precision: 6);
    }

    [Fact]
    public void ReleasedCorpseWithinTenMetresOfThePlayerIsRemoved()
    {
        (FakeHost host, var trackerHost, _) = Make();
        const uint cellId = 0x7D64001Fu;
        host.Automation.Objects.Objects.Add(new PluginWorldObject(2u, 0u, "Corpse of Acdream", PluginObjectClass.Corpse, 0u, 0u, 0u)
        {
            HasAppraisalData = false,
            Position = CompassPosition(cellId, 100d, 50d),
        });
        host.Events.RaiseObjectChanged(2u, PluginObjectChangeKind.Created);
        Assert.Single(trackerHost.Tracker.Items);

        // Player standing 5 metres away, same cell.
        host.Automation.Navigation.Snapshot = new PluginNavigationSnapshot(
            true, false, 999u, CompassPosition(cellId, 105d, 50d), false, false);

        host.Events.RaiseObjectChanged(2u, PluginObjectChangeKind.Released);

        Assert.Empty(trackerHost.Tracker.Items);
    }

    [Fact]
    public void ReleasedCorpseFarFromThePlayerIsNotRemoved()
    {
        (FakeHost host, var trackerHost, _) = Make();
        const uint cellId = 0x7D64001Fu;
        host.Automation.Objects.Objects.Add(new PluginWorldObject(3u, 0u, "Corpse of Acdream", PluginObjectClass.Corpse, 0u, 0u, 0u)
        {
            HasAppraisalData = false,
            Position = CompassPosition(cellId, 100d, 50d),
        });
        host.Events.RaiseObjectChanged(3u, PluginObjectChangeKind.Created);

        // Player standing 50 metres away, same cell.
        host.Automation.Navigation.Snapshot = new PluginNavigationSnapshot(
            true, false, 999u, CompassPosition(cellId, 150d, 50d), false, false);

        host.Events.RaiseObjectChanged(3u, PluginObjectChangeKind.Released);

        Assert.Single(trackerHost.Tracker.Items);
    }
}
