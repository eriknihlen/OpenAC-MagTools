using OpenAC.MagTools.Trackers.Corpse;

namespace OpenAC.MagTools.Tests.Trackers.Corpse;

public sealed class CorpseTrackerRowsTests
{
    [Fact]
    public void NewestIsFirstAndOpenedCorpsesAreExcluded()
    {
        var older = new TrackedCorpse(1u, new DateTime(2026, 9, 16, 10, 0, 0), 1, 10, 20, 0, "Corpse of Acdream");
        var newer = new TrackedCorpse(2u, new DateTime(2026, 9, 16, 11, 0, 0), 1, 10, 20, 0, "Corpse of a Drudge");
        var opened = new TrackedCorpse(3u, new DateTime(2026, 9, 16, 12, 0, 0), 1, 10, 20, 0, "Corpse of a Rat", opened: true);

        var t = new CorpseTracker(() => new DateTime(2026, 9, 16, 12, 0, 0));
        t.ImportStats([older, newer, opened], "Acdream");

        var rows = CorpseTrackerRows.Build(t);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Corpse of a Drudge", rows[0].Name);
        Assert.Equal("Corpse of Acdream", rows[1].Name);
        Assert.DoesNotContain(rows, row => row.Name == "Corpse of a Rat");
    }
}
