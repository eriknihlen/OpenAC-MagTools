using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Trackers.Shared;

namespace OpenAC.MagTools.Tests.Trackers.Shared;

public sealed class HostPositionTests
{
    [Fact]
    public void ToLandblockLocalRoundTripsBackToTheSameCompassValueViaCoordsFromLandblock()
    {
        // A live host position for cell 0x7D64001F, well inside the
        // landblock (local x=100, y=50 metres), in the host's own compass
        // units per AppAutomationSurface.ProjectNavigationPosition.
        const uint cellId = 0x7D64001F;
        const uint blockX = 0x7D, blockY = 0x64;
        double localX = 100d, localY = 50d;
        double compassEw = ((blockX - 127d) * 192d + localX - 84d) / 240d;
        double compassNs = ((blockY - 127d) * 192d + localY - 84d) / 240d;

        var position = new PluginNavigationPosition(cellId, compassEw, compassNs, 0d, 0f, true);

        LandblockLocalPosition local = HostPosition.ToLandblockLocal(position);

        Assert.Equal((int)cellId, local.CellId);
        Assert.Equal(localX, local.X, precision: 6);
        Assert.Equal(localY, local.Y, precision: 6);

        // Round-tripping through Coords.FromLandblock (the same math the
        // corpse/player trackers use to display/compare positions) must
        // reproduce the exact compass values the host started with.
        Coords roundTrip = Coords.FromLandblock(local.CellId, local.X, local.Y);
        Assert.Equal(compassNs, roundTrip.Latitude, precision: 6);
        Assert.Equal(compassEw, roundTrip.Longitude, precision: 6);
    }

    [Fact]
    public void ElevationRoundTripsThroughThe240Scale()
    {
        var position = new PluginNavigationPosition(0x7D64001Fu, 0d, 0d, 12.5d, 0f, true);

        LandblockLocalPosition local = HostPosition.ToLandblockLocal(position);

        Assert.Equal(12.5d * 240d, local.Z, precision: 6);
    }
}
