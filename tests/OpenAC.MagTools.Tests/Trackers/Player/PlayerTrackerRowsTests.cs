using OpenAC.MagTools.Trackers.Player;

namespace OpenAC.MagTools.Tests.Trackers.Player;

public sealed class PlayerTrackerRowsTests
{
    [Fact]
    public void NewArrivalInsertsAtTheTopImmediately()
    {
        var clock = new DateTime(2026, 9, 16, 12, 0, 0);
        var tracker = new PlayerTracker(() => clock);
        var rows = new PlayerTrackerRowsBuilder(() => clock);

        tracker.ProcessWorldObject(new PlayerObservation(1u, "Alice", 1, 0, 0, 0), "Acdream");
        rows.Build(tracker); // primes _lastSort

        tracker.ProcessWorldObject(new PlayerObservation(2u, "Bob", 1, 0, 0, 0), "Acdream");
        var result = rows.Build(tracker);

        Assert.Equal("Bob", result[0].Name);
    }

    [Fact]
    public void ReSortIsRateLimitedToOncePerTenSeconds()
    {
        var clock = new DateTime(2026, 9, 16, 12, 0, 0);
        var tracker = new PlayerTracker(() => clock);
        var rows = new PlayerTrackerRowsBuilder(() => clock);

        tracker.ProcessWorldObject(new PlayerObservation(1u, "Alice", 1, 0, 0, 0), "Acdream");
        rows.Build(tracker); // sorts once (Alice on top by insertion, also newest)

        clock = clock.AddSeconds(1);
        tracker.ProcessWorldObject(new PlayerObservation(2u, "Bob", 1, 0, 0, 0), "Acdream"); // Bob is newer

        // Alice is re-seen (an existing entry, so no re-insertion at top --
        // only a brand-new name jumps the queue) 1s later, still within the
        // 10s window: Bob (inserted at top when he first arrived) should
        // still be above Alice, since the rate-limited sort hasn't run yet.
        clock = clock.AddSeconds(1);
        tracker.ProcessWorldObject(new PlayerObservation(1u, "Alice", 1, 0, 0, 0), "Acdream");
        var withinWindow = rows.Build(tracker);

        Assert.Equal("Bob", withinWindow[0].Name);

        // After 10s, the real sort by LastSeen runs: Alice was seen last (2s in), Bob at 1s in.
        clock = clock.AddSeconds(11);
        var afterWindow = rows.Build(tracker);

        Assert.Equal("Alice", afterWindow[0].Name);
        Assert.Equal("Bob", afterWindow[1].Name);
    }
}
