using OpenAC.MagTools.Trackers.Corpse;

namespace OpenAC.MagTools.Tests.Trackers.Corpse;

public sealed class CorpseTrackerTests
{
    private static readonly DateTime FixedNow = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Local);

    private static CorpseTracker MakeTracker(DateTime? now = null)
        => new(() => now ?? FixedNow);

    [Fact]
    public void MyOwnCorpseNameTracksImmediately()
    {
        var tracker = MakeTracker();
        var requested = new List<uint>();

        tracker.ProcessWorldObject(
            new CorpseObservation(100u, 1, 10, 20, 0, "Corpse of Acdream", 0, false, null),
            "Acdream",
            trackAllCorpses: false,
            requestId: requested.Add);

        Assert.Single(tracker.Items);
        Assert.Equal("Corpse of Acdream", tracker.Items[0].Description);
        Assert.Empty(requested);
    }

    [Fact]
    public void TrackAllCorpsesTracksAnyCorpse()
    {
        var tracker = MakeTracker();
        tracker.ProcessWorldObject(
            new CorpseObservation(101u, 1, 10, 20, 0, "Corpse of a Drudge", 0, false, null),
            "Acdream",
            trackAllCorpses: true,
            requestId: _ => { });

        Assert.Single(tracker.Items);
    }

    [Fact]
    public void UnrelatedCorpseBelowBurdenThresholdIsIgnored()
    {
        var tracker = MakeTracker();
        tracker.ProcessWorldObject(
            new CorpseObservation(102u, 1, 10, 20, 0, "Corpse of a Drudge", 500, true, "This is the corpse of a drudge, killed by Acdream."),
            "Acdream",
            trackAllCorpses: false,
            requestId: _ => { });

        Assert.Empty(tracker.Items);
    }

    [Fact]
    public void HeavyCorpseWithoutIdDataRequestsIdAndDoesNotTrackYet()
    {
        var tracker = MakeTracker();
        var requested = new List<uint>();

        tracker.ProcessWorldObject(
            new CorpseObservation(103u, 1, 10, 20, 0, "Corpse of a Drudge", 7000, false, null),
            "Acdream",
            trackAllCorpses: false,
            requestId: requested.Add);

        Assert.Empty(tracker.Items);
        Assert.Equal([103u], requested);
    }

    [Fact]
    public void HeavyCorpseWithFullDescriptionNamingMeTracksOnceIdentified()
    {
        var tracker = MakeTracker();

        tracker.ProcessWorldObject(
            new CorpseObservation(104u, 1, 10, 20, 0, "Corpse of a Drudge", 7000, true, "This is the corpse of a drudge, killed by Acdream."),
            "Acdream",
            trackAllCorpses: false,
            requestId: _ => { });

        Assert.Single(tracker.Items);
    }

    [Fact]
    public void HeavyCorpseWithFullDescriptionNotNamingMeIsIgnored()
    {
        var tracker = MakeTracker();

        tracker.ProcessWorldObject(
            new CorpseObservation(105u, 1, 10, 20, 0, "Corpse of a Drudge", 7000, true, "This is the corpse of a drudge, killed by Someoneelse."),
            "Acdream",
            trackAllCorpses: false,
            requestId: _ => { });

        Assert.Empty(tracker.Items);
    }

    [Fact]
    public void GuidReuseAtADifferentLandblockRemovesTheStaleEntry()
    {
        var tracker = MakeTracker();
        TrackedCorpse? removed = null;
        tracker.ItemRemoved += item => removed = item;

        tracker.ProcessWorldObject(
            new CorpseObservation(106u, 1, 10, 20, 0, "Corpse of Acdream", 0, false, null),
            "Acdream", false, _ => { });

        tracker.ProcessWorldObject(
            new CorpseObservation(106u, 2, 10, 20, 0, "Corpse of a Drudge", 0, false, null),
            "Acdream", true, _ => { });

        Assert.NotNull(removed);
        Assert.Equal(106u, removed!.Id);
        Assert.Single(tracker.Items);
        Assert.Equal("Corpse of a Drudge", tracker.Items[0].Description);
    }

    [Fact]
    public void GuidReuseWhenMovedMoreThanOneUnitRemovesTheStaleEntry()
    {
        var tracker = MakeTracker();
        tracker.ProcessWorldObject(
            new CorpseObservation(107u, 1, 10, 20, 0, "Corpse of Acdream", 0, false, null),
            "Acdream", false, _ => { });

        tracker.ProcessWorldObject(
            new CorpseObservation(107u, 1, 15, 20, 0, "Corpse of a Drudge", 0, false, null),
            "Acdream", true, _ => { });

        Assert.Single(tracker.Items);
        Assert.Equal("Corpse of a Drudge", tracker.Items[0].Description);
    }

    [Fact]
    public void AlreadyTrackedCorpseAtTheSameSpotIsANoOp()
    {
        var tracker = MakeTracker();
        int adds = 0;
        tracker.ItemAdded += _ => adds++;

        tracker.ProcessWorldObject(
            new CorpseObservation(108u, 1, 10, 20, 0, "Corpse of Acdream", 0, false, null),
            "Acdream", false, _ => { });
        tracker.ProcessWorldObject(
            new CorpseObservation(108u, 1, 10.5, 20, 0, "Corpse of Acdream", 0, false, null),
            "Acdream", false, _ => { });

        Assert.Equal(1, adds);
        Assert.Single(tracker.Items);
    }

    [Fact]
    public void ContainerOpenedMarksTheCorpseOpenedOnce()
    {
        var tracker = MakeTracker();
        tracker.ProcessWorldObject(
            new CorpseObservation(109u, 1, 10, 20, 0, "Corpse of Acdream", 0, false, null),
            "Acdream", false, _ => { });

        int changes = 0;
        tracker.ItemChanged += _ => changes++;

        tracker.OnContainerOpened(109u);
        tracker.OnContainerOpened(109u); // second open is a no-op (already Opened)

        Assert.Equal(1, changes);
        Assert.True(tracker.Items[0].Opened);
    }

    [Fact]
    public void OnReleasedNearbyRemovesTheTrackedCorpse()
    {
        var tracker = MakeTracker();
        tracker.ProcessWorldObject(
            new CorpseObservation(110u, 1, 10, 20, 0, "Corpse of Acdream", 0, false, null),
            "Acdream", false, _ => { });

        tracker.OnReleasedNearby(110u);

        Assert.Empty(tracker.Items);
    }

    [Fact]
    public void RetentionKeepsMyOwnCorpseForSevenDaysAndOthersForTwoHours()
    {
        DateTime start = FixedNow;
        var tracker = new CorpseTracker(() => start);

        tracker.ProcessWorldObject(
            new CorpseObservation(111u, 1, 10, 20, 0, "Corpse of Acdream", 0, false, null),
            "Acdream", false, _ => { });
        tracker.ProcessWorldObject(
            new CorpseObservation(112u, 1, 10, 20, 0, "Corpse of a Drudge", 0, false, null),
            "Acdream", true, _ => { });

        // 3 hours later: the drudge corpse (2h retention) is gone, mine (7d) remains.
        start = FixedNow.AddHours(3);
        tracker.RunMaintenance("Acdream");

        Assert.Single(tracker.Items);
        Assert.Equal("Corpse of Acdream", tracker.Items[0].Description);

        // 8 days later: even my own corpse is gone.
        start = FixedNow.AddDays(8);
        tracker.RunMaintenance("Acdream");

        Assert.Empty(tracker.Items);
    }

    [Fact]
    public void ImportSkipsEntriesFailingRetentionAndAlreadyTrackedIds()
    {
        var tracker = MakeTracker();
        tracker.ProcessWorldObject(
            new CorpseObservation(113u, 1, 10, 20, 0, "Corpse of Acdream", 0, false, null),
            "Acdream", false, _ => { });

        var stale = new TrackedCorpse(999u, FixedNow.AddDays(-10), 1, 0, 0, 0, "Corpse of a Drudge");
        var duplicate = new TrackedCorpse(113u, FixedNow, 1, 10, 20, 0, "Corpse of Acdream (dup)");
        var fresh = new TrackedCorpse(114u, FixedNow, 1, 0, 0, 0, "Corpse of a Drudge");

        tracker.ImportStats([stale, duplicate, fresh], "Acdream");

        Assert.Equal(2, tracker.Items.Count);
        Assert.Contains(tracker.Items, item => item.Id == 113u);
        Assert.Contains(tracker.Items, item => item.Id == 114u);
    }
}
