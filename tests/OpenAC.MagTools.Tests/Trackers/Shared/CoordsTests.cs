using OpenAC.MagTools.Trackers.Shared;

namespace OpenAC.MagTools.Tests.Trackers.Shared;

public sealed class CoordsTests
{
    [Fact]
    public void FromLandblockMatchesTheHandComputedGolden()
    {
        // 0x7D64001F: lbLng = 0x7D = 125, lbLat = 0x64 = 100.
        // lat = ((100-127)*192 + 84-84)/240 = -21.6 -> 21.6S
        // lng = ((125-127)*192 + 84-84)/240 = -1.6  -> 1.6W
        Coords coords = Coords.FromLandblock(0x7D64001F, x: 84, y: 84);

        Assert.Equal(-21.6, coords.Latitude, precision: 6);
        Assert.Equal(-1.6, coords.Longitude, precision: 6);
        Assert.Equal("21.6S, 1.6W", coords.ToString());
    }
}
