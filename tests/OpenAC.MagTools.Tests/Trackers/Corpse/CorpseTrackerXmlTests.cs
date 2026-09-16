using OpenAC.MagTools.Trackers.Corpse;

namespace OpenAC.MagTools.Tests.Trackers.Corpse;

public sealed class CorpseTrackerXmlTests
{
    [Fact]
    public void ExportThenImportRoundTripsEveryField()
    {
        var item = new TrackedCorpse(42u, new DateTime(2026, 9, 16, 10, 30, 0), 1, 11.5, 22.25, 3.0, "Corpse of Acdream", opened: true);

        string xml = CorpseTrackerXml.Export([item]);
        bool ok = CorpseTrackerXml.TryImport(xml, out List<TrackedCorpse> imported);

        Assert.True(ok);
        TrackedCorpse round = Assert.Single(imported);
        Assert.Equal(item.Id, round.Id);
        Assert.Equal(item.TimeStamp, round.TimeStamp);
        Assert.Equal(item.LandBlock, round.LandBlock);
        Assert.Equal(item.LocationX, round.LocationX);
        Assert.Equal(item.LocationY, round.LocationY);
        Assert.Equal(item.LocationZ, round.LocationZ);
        Assert.Equal(item.Description, round.Description);
        Assert.Equal(item.Opened, round.Opened);
    }

    [Fact]
    public void CorruptContentIsReportedAsNotImported()
    {
        bool ok = CorpseTrackerXml.TryImport("not xml at all <<<", out List<TrackedCorpse> imported);

        Assert.False(ok);
        Assert.Empty(imported);
    }

    [Fact]
    public void EmptyContentIsReportedAsNotImported()
    {
        bool ok = CorpseTrackerXml.TryImport(string.Empty, out List<TrackedCorpse> imported);

        Assert.False(ok);
        Assert.Empty(imported);
    }
}
