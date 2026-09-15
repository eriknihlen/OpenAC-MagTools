using OpenAC.MagTools.Tests.Fakes;

namespace OpenAC.MagTools.Tests;

public sealed class TickSchedulerTests
{
    private readonly FakeHost _host = new();

    private TickScheduler NewScheduler()
        => new(_host.Events, new ChatOutput(_host));

    [Fact]
    public void TheSchedulerSubscribesAndUnsubscribesTheHostTick()
    {
        TickScheduler scheduler = NewScheduler();
        Assert.Equal(1, _host.Events.TickSubscriberCount);

        scheduler.Dispose();
        Assert.Equal(0, _host.Events.TickSubscriberCount);
    }

    [Fact]
    public void ARepeatingCallbackFiresOncePerInterval()
    {
        using TickScheduler scheduler = NewScheduler();
        int calls = 0;
        scheduler.Every(TimeSpan.FromSeconds(1), () => calls++);

        _host.Events.RaiseTick(0.5d);
        Assert.Equal(0, calls);

        _host.Events.RaiseTick(0.6d);
        Assert.Equal(1, calls);

        _host.Events.RaiseTick(1.0d);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ARepeatingCallbackCanBeCancelled()
    {
        using TickScheduler scheduler = NewScheduler();
        int calls = 0;
        IDisposable entry = scheduler.Every(TimeSpan.FromSeconds(1), () => calls++);

        _host.Events.RaiseTick(1.5d);
        Assert.Equal(1, calls);

        entry.Dispose();
        _host.Events.RaiseTick(5d);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ADelayFiresExactlyOnce()
    {
        using TickScheduler scheduler = NewScheduler();
        int calls = 0;
        scheduler.Delay(TimeSpan.FromSeconds(2), () => calls++);

        _host.Events.RaiseTick(1d);
        Assert.Equal(0, calls);

        _host.Events.RaiseTick(1.5d);
        Assert.Equal(1, calls);

        _host.Events.RaiseTick(10d);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void RunOnNextTickRunsOnceOnTheFollowingTick()
    {
        using TickScheduler scheduler = NewScheduler();
        var order = new List<string>();
        scheduler.RunOnNextTick(() => order.Add("deferred"));

        _host.Events.RaiseTick(0.016d);
        _host.Events.RaiseTick(0.016d);

        Assert.Equal(["deferred"], order);
    }

    [Fact]
    public void WorkQueuedFromInsideATickWaitsForTheNextOne()
    {
        using TickScheduler scheduler = NewScheduler();
        var order = new List<string>();
        scheduler.RunOnNextTick(() =>
        {
            order.Add("first");
            scheduler.RunOnNextTick(() => order.Add("second"));
        });

        _host.Events.RaiseTick(0.016d);
        Assert.Equal(["first"], order);

        _host.Events.RaiseTick(0.016d);
        Assert.Equal(["first", "second"], order);
    }

    [Fact]
    public void AThrowingCallbackIsReportedAndTheOthersStillRun()
    {
        using TickScheduler scheduler = NewScheduler();
        int survivor = 0;
        scheduler.Every(TimeSpan.FromSeconds(1), static () =>
            throw new InvalidOperationException("boom"));
        scheduler.Every(TimeSpan.FromSeconds(1), () => survivor++);

        _host.Events.RaiseTick(1.5d);

        Assert.Equal(1, survivor);
        Assert.Contains(
            _host.ChatLines,
            line => line.StartsWith(
                ChatOutput.Prefix + "Exception caught: boom",
                StringComparison.Ordinal));
    }

    [Fact]
    public void AThrowingCallbackKeepsFiringOnLaterTicks()
    {
        using TickScheduler scheduler = NewScheduler();
        int attempts = 0;
        scheduler.Every(TimeSpan.FromSeconds(1), () =>
        {
            attempts++;
            throw new InvalidOperationException("boom");
        });

        _host.Events.RaiseTick(1.5d);
        _host.Events.RaiseTick(1.5d);

        Assert.Equal(2, attempts);
    }

    [Fact]
    public void ElapsedSecondsAccumulates()
    {
        using TickScheduler scheduler = NewScheduler();

        _host.Events.RaiseTick(0.5d);
        _host.Events.RaiseTick(0.25d);

        Assert.Equal(0.75d, scheduler.ElapsedSeconds, 6);
    }

    [Fact]
    public void ANegativeOrNaNTickIsTreatedAsZero()
    {
        using TickScheduler scheduler = NewScheduler();

        _host.Events.RaiseTick(-1d);
        _host.Events.RaiseTick(double.NaN);

        Assert.Equal(0d, scheduler.ElapsedSeconds);
    }

    [Fact]
    public void ARepeatingCallbackNeedsAPositiveInterval()
    {
        using TickScheduler scheduler = NewScheduler();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => scheduler.Every(TimeSpan.Zero, static () => { }));
    }

    [Fact]
    public void ADisposedSchedulerIgnoresLaterTicks()
    {
        TickScheduler scheduler = NewScheduler();
        int calls = 0;
        scheduler.Every(TimeSpan.FromSeconds(1), () => calls++);
        scheduler.Dispose();

        scheduler.Tick(5d);
        Assert.Equal(0, calls);
    }
}
