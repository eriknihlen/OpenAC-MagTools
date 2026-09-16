using OpenAC.MagTools.Trackers.Player;

namespace OpenAC.MagTools.Tests.Trackers.Player;

public sealed class PlayerTrackerXmlTests
{
    [Fact]
    public void ExportThenImportRoundTripsEveryField()
    {
        var item = new TrackedPlayer("Foo", new DateTime(2026, 9, 16, 10, 30, 0), 1, 11.5, 22.25, 3.0, 42u);

        string xml = PlayerTrackerXml.Export([item]);
        bool ok = PlayerTrackerXml.TryImport(xml, out List<TrackedPlayer> imported);

        Assert.True(ok);
        TrackedPlayer round = Assert.Single(imported);
        Assert.Equal(item.Name, round.Name);
        Assert.Equal(item.LastSeen, round.LastSeen);
        Assert.Equal(item.LandBlock, round.LandBlock);
        Assert.Equal(item.LocationX, round.LocationX);
        Assert.Equal(item.LocationY, round.LocationY);
        Assert.Equal(item.LocationZ, round.LocationZ);
        // The XML shape (per the port design's §2.3) has no Id attribute --
        // players are keyed by name, so a re-imported entry's guid is 0 until
        // the next sighting updates it.
        Assert.Equal(0u, round.Id);
    }

    [Fact]
    public void CorruptContentIsReportedAsNotImported()
    {
        bool ok = PlayerTrackerXml.TryImport("<<<not xml", out List<TrackedPlayer> imported);

        Assert.False(ok);
        Assert.Empty(imported);
    }
}
