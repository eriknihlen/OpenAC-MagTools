using OpenAC.MagTools.Commands;
using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using Xunit;

namespace OpenAC.MagTools.Tests.Macros;

public sealed class QueuedCommandRunnerTests
{
    private static (FakeHost Host, QueuedCommandRunner Runner, OpenAC.MagTools.TickScheduler Scheduler) Build()
    {
        var host = new FakeHost();
        var chat = new ChatOutput(host);
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var router = new MtCommandRouter(host, chat, settings);
        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, chat);
        var runner = new QueuedCommandRunner(host, router);
        runner.Bind(scheduler);
        return (host, runner, scheduler);
    }

    [Fact]
    public void DispatchesOnePerTick()
    {
        (FakeHost host, QueuedCommandRunner runner, OpenAC.MagTools.TickScheduler scheduler) = Build();

        runner.Enqueue("one");
        runner.Enqueue("two");

        scheduler.Tick(0.1);
        Assert.Equal(["one"], host.Automation.Chat.Submitted);

        scheduler.Tick(0.1);
        Assert.Equal(["one", "two"], host.Automation.Chat.Submitted);
    }

    [Fact]
    public void ClearThenEnqueueInTheSameTickStillDispatchesOnlyOnePerTick()
    {
        // L1: a Stop() (Clear()) immediately followed by a Run() (fresh
        // Enqueue calls) in the same synthetic frame -- e.g. a reconnect's
        // Logoff-then-LoginComplete landing back to back before the next
        // real tick -- must not leave two concurrent drain chains armed.
        // Without an explicit "already draining" guard, the runner's first
        // Enqueue schedules chain #1; Clear() empties the queue but the
        // scheduled callback is still pending; a second batch of Enqueue
        // calls (seeing an empty queue) schedules a SECOND chain #2. Both
        // chains fire on the very next tick, dispatching two commands
        // instead of one.
        (FakeHost host, QueuedCommandRunner runner, OpenAC.MagTools.TickScheduler scheduler) = Build();

        runner.Enqueue("stale"); // chain #1 armed

        runner.Clear(); // Stop(): queue empties, chain #1 is still pending
        runner.Enqueue("first"); // Run(): must NOT arm a second chain
        runner.Enqueue("second");
        runner.Enqueue("third");

        scheduler.Tick(0.1);
        Assert.Equal(["first"], host.Automation.Chat.Submitted);

        scheduler.Tick(0.1);
        Assert.Equal(["first", "second"], host.Automation.Chat.Submitted);

        scheduler.Tick(0.1);
        Assert.Equal(["first", "second", "third"], host.Automation.Chat.Submitted);
    }

    [Fact]
    public void ClearDropsEverythingWithoutDispatching()
    {
        (FakeHost host, QueuedCommandRunner runner, OpenAC.MagTools.TickScheduler scheduler) = Build();

        runner.Enqueue("never");
        runner.Clear();

        scheduler.Tick(0.1);
        scheduler.Tick(0.1);

        Assert.Empty(host.Automation.Chat.Submitted);
        Assert.Equal(0, runner.Count);
    }

    [Fact]
    public void ANullEntryIsADummySlotThatAdvancesWithoutDispatching()
    {
        (FakeHost host, QueuedCommandRunner runner, OpenAC.MagTools.TickScheduler scheduler) = Build();

        runner.Enqueue(null);
        runner.Enqueue("real");

        scheduler.Tick(0.1); // dummy
        Assert.Empty(host.Automation.Chat.Submitted);

        scheduler.Tick(0.1); // real
        Assert.Equal(["real"], host.Automation.Chat.Submitted);
    }

    [Fact]
    public void DrainingAgainAfterFullyDrainingStartsAFreshChain()
    {
        // Once a chain fully drains (queue empties and no reschedule
        // happens), a later Enqueue must arm a NEW chain rather than
        // staying silent forever.
        (FakeHost host, QueuedCommandRunner runner, OpenAC.MagTools.TickScheduler scheduler) = Build();

        runner.Enqueue("first");
        scheduler.Tick(0.1);
        Assert.Equal(["first"], host.Automation.Chat.Submitted);

        runner.Enqueue("second");
        scheduler.Tick(0.1);
        Assert.Equal(["first", "second"], host.Automation.Chat.Submitted);
    }
}
