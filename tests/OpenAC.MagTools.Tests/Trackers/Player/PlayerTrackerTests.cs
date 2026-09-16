using OpenAC.MagTools.Trackers.Player;

namespace OpenAC.MagTools.Tests.Trackers.Player;

public sealed class PlayerTrackerTests
{
    private static readonly DateTime FixedNow = new(2026, 9, 16, 12, 0, 0);

    [Fact]
    public void NeverTracksMyself()
    {
        var tracker = new PlayerTracker(() => FixedNow);
        tracker.ProcessWorldObject(new PlayerObservation(1u, "Acdream", 1, 10, 20, 0), "Acdream");

        Assert.Empty(tracker.Items);
    }

    [Fact]
    public void FirstSightingAddsANewEntry()
    {
        var tracker = new PlayerTracker(() => FixedNow);
        TrackedPlayer? added = null;
        tracker.ItemAdded += item => added = item;

        tracker.ProcessWorldObject(new PlayerObservation(2u, "Foo", 1, 10, 20, 0), "Acdream");

        Assert.NotNull(added);
        Assert.Equal("Foo", added!.Name);
        Assert.Equal(FixedNow, added.LastSeen);
        Assert.Single(tracker.Items);
    }

    [Fact]
    public void SecondSightingUpdatesTheSameEntryByNameNotGuid()
    {
        var tracker = new PlayerTracker(() => FixedNow);
        tracker.ProcessWorldObject(new PlayerObservation(2u, "Foo", 1, 10, 20, 0), "Acdream");

        TrackedPlayer? changed = null;
        tracker.ItemChanged += item => changed = item;

        // A different guid (relog) but the same name still updates in place.
        tracker.ProcessWorldObject(new PlayerObservation(3u, "Foo", 1, 15, 25, 1), "Acdream");

        Assert.NotNull(changed);
        Assert.Single(tracker.Items);
        Assert.Equal(3u, tracker.Items[0].Id);
        Assert.Equal(15, tracker.Items[0].LocationX);
    }

    [Fact]
    public void NoExpiryOnlyClearHistoryRemovesEntries()
    {
        var tracker = new PlayerTracker(() => FixedNow);
        tracker.ProcessWorldObject(new PlayerObservation(2u, "Foo", 1, 10, 20, 0), "Acdream");

        int removed = 0;
        tracker.ItemRemoved += _ => removed++;

        tracker.ClearStats();

        Assert.Equal(1, removed);
        Assert.Empty(tracker.Items);
    }

    [Fact]
    public void ImportSkipsAlreadyTrackedNames()
    {
        var tracker = new PlayerTracker(() => FixedNow);
        tracker.ProcessWorldObject(new PlayerObservation(2u, "Foo", 1, 10, 20, 0), "Acdream");

        var duplicate = new TrackedPlayer("Foo", FixedNow, 1, 0, 0, 0, 99u);
        var fresh = new TrackedPlayer("Bar", FixedNow, 1, 0, 0, 0, 100u);
        tracker.ImportStats([duplicate, fresh]);

        Assert.Equal(2, tracker.Items.Count);
        Assert.Equal(2u, tracker.Items.Single(item => item.Name == "Foo").Id);
    }
}
