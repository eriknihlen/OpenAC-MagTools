using OpenAC.MagTools.Commands;
using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Tests.Trackers.Combat;
using Xunit;

namespace OpenAC.MagTools.Tests.Macros;

public sealed class PeriodicCommandsTests
{
    private static (FakeHost Host, PeriodicCommands Macro, OpenAC.MagTools.TickScheduler Scheduler,
        ScopedCommandStore Store, FakeTimeProvider Clock) Build()
    {
        var host = new FakeHost();
        var chat = new ChatOutput(host);
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var router = new MtCommandRouter(host, chat, settings);
        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, chat);
        var clock = new FakeTimeProvider();
        var macro = new PeriodicCommands(host, router, settings.Commands, clock);
        return (host, macro, scheduler, settings.Commands, clock);
    }

    [Fact]
    public void FiresWhenMinutesAfterMidnightPlusOffsetIsAMultipleOfInterval()
    {
        (FakeHost host, PeriodicCommands macro, OpenAC.MagTools.TickScheduler scheduler,
            ScopedCommandStore store, FakeTimeProvider clock) = Build();

        // 00:05 UTC -> 5 minutes after midnight; interval 5, offset 0 -> (5+0)%5==0 -> fires.
        clock.Now = new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero);
        string scope = SettingsScope.Character("acct", "Server", "Acdream");
        store.SetPeriodicCommands(scope, [new PeriodicCommand("/mt test", TimeSpan.FromMinutes(5), TimeSpan.Zero)]);

        macro.StartServerScope(scheduler, SettingsScope.Server("Server"));
        macro.SetCharacterScope(scope);
        macro.OnTimer();
        scheduler.Tick(0.1); // drains the one matched command

        Assert.Equal(0, macro.PendingCount);
    }

    [Fact]
    public void DoesNotFireWhenNotOnTheIntervalGrid()
    {
        (FakeHost host, PeriodicCommands macro, OpenAC.MagTools.TickScheduler scheduler,
            ScopedCommandStore store, FakeTimeProvider clock) = Build();

        clock.Now = new DateTimeOffset(2026, 1, 1, 0, 6, 0, TimeSpan.Zero); // 6 minutes -> 6%5 != 0
        string scope = SettingsScope.Character("acct", "Server", "Acdream");
        store.SetPeriodicCommands(scope, [new PeriodicCommand("/mt test", TimeSpan.FromMinutes(5), TimeSpan.Zero)]);

        macro.StartServerScope(scheduler, SettingsScope.Server("Server"));
        macro.SetCharacterScope(scope);
        macro.OnTimer();

        Assert.Equal(0, macro.PendingCount);
    }

    [Fact]
    public void OffsetShiftsTheGrid()
    {
        (FakeHost host, PeriodicCommands macro, OpenAC.MagTools.TickScheduler scheduler,
            ScopedCommandStore store, FakeTimeProvider clock) = Build();

        // 00:07 -> 7 minutes; interval 5, offset 3 -> (7+3)%5==0 -> fires.
        clock.Now = new DateTimeOffset(2026, 1, 1, 0, 7, 0, TimeSpan.Zero);
        string scope = SettingsScope.Character("acct", "Server", "Acdream");
        store.SetPeriodicCommands(
            scope,
            [new PeriodicCommand("/mt test", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(3))]);

        macro.StartServerScope(scheduler, SettingsScope.Server("Server"));
        macro.SetCharacterScope(scope);
        macro.OnTimer();
        scheduler.Tick(0.1);

        Assert.Equal(0, macro.PendingCount);
    }

    [Fact]
    public void TheSameWallClockMinuteNeverEvaluatesTwice()
    {
        (FakeHost host, PeriodicCommands macro, OpenAC.MagTools.TickScheduler scheduler,
            ScopedCommandStore store, FakeTimeProvider clock) = Build();

        clock.Now = new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero);
        string scope = SettingsScope.Character("acct", "Server", "Acdream");
        store.SetPeriodicCommands(scope, [new PeriodicCommand("say hi", TimeSpan.FromMinutes(5), TimeSpan.Zero)]);
        macro.StartServerScope(scheduler, SettingsScope.Server("Server"));
        macro.SetCharacterScope(scope);

        // The 20-second timer polls up to three times in the same minute --
        // only the first poll should actually evaluate/enqueue.
        macro.OnTimer();
        clock.Now += TimeSpan.FromSeconds(20);
        macro.OnTimer();
        clock.Now += TimeSpan.FromSeconds(20);
        macro.OnTimer();

        scheduler.Tick(0.1);
        Assert.Equal(0, macro.PendingCount);
        Assert.Single(host.Automation.Chat.Submitted);
    }

    [Fact]
    public void ANewMinuteEvaluatesAgain()
    {
        (FakeHost host, PeriodicCommands macro, OpenAC.MagTools.TickScheduler scheduler,
            ScopedCommandStore store, FakeTimeProvider clock) = Build();

        clock.Now = new DateTimeOffset(2026, 1, 1, 0, 1, 0, TimeSpan.Zero);
        string scope = SettingsScope.Character("acct", "Server", "Acdream");
        store.SetPeriodicCommands(scope, [new PeriodicCommand("say hi", TimeSpan.FromMinutes(1), TimeSpan.Zero)]);
        macro.StartServerScope(scheduler, SettingsScope.Server("Server"));
        macro.SetCharacterScope(scope);

        macro.OnTimer();
        scheduler.Tick(0.1);
        clock.Now += TimeSpan.FromMinutes(1);
        macro.OnTimer();
        scheduler.Tick(0.1);

        Assert.Equal(["say hi", "say hi"], host.Automation.Chat.Submitted);
    }

    [Fact]
    public void CharacterScopeCommandsQueueBeforeServerScopeCommands()
    {
        (FakeHost host, PeriodicCommands macro, OpenAC.MagTools.TickScheduler scheduler,
            ScopedCommandStore store, FakeTimeProvider clock) = Build();

        clock.Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string characterScope = SettingsScope.Character("acct", "Server", "Acdream");
        string serverScope = SettingsScope.Server("Server");
        store.SetPeriodicCommands(characterScope, [new PeriodicCommand("char", TimeSpan.FromMinutes(1), TimeSpan.Zero)]);
        store.SetPeriodicCommands(serverScope, [new PeriodicCommand("server", TimeSpan.FromMinutes(1), TimeSpan.Zero)]);

        macro.StartServerScope(scheduler, serverScope);
        macro.SetCharacterScope(characterScope);
        macro.OnTimer();
        scheduler.Tick(0.1);
        scheduler.Tick(0.1);

        Assert.Equal(["char", "server"], host.Automation.Chat.Submitted);
    }

    [Fact]
    public void StartServerScopeStartsTheTimerWithoutACharacterScopeYet()
    {
        // MEDIUM-4: the timer starts as soon as WorldName is known (server
        // scope only), well before the character name has to resolve.
        (FakeHost host, PeriodicCommands macro, OpenAC.MagTools.TickScheduler scheduler,
            ScopedCommandStore store, FakeTimeProvider clock) = Build();

        clock.Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string serverScope = SettingsScope.Server("Server");
        store.SetPeriodicCommands(serverScope, [new PeriodicCommand("server", TimeSpan.FromMinutes(1), TimeSpan.Zero)]);

        macro.StartServerScope(scheduler, serverScope);
        macro.OnTimer();
        scheduler.Tick(0.1);

        Assert.Equal(["server"], host.Automation.Chat.Submitted);
    }

    [Fact]
    public void SetCharacterScopeBackfillsWithoutDisturbingTheRunningTimer()
    {
        (FakeHost host, PeriodicCommands macro, OpenAC.MagTools.TickScheduler scheduler,
            ScopedCommandStore store, FakeTimeProvider clock) = Build();

        clock.Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string characterScope = SettingsScope.Character("acct", "Server", "Acdream");
        string serverScope = SettingsScope.Server("Server");
        store.SetPeriodicCommands(characterScope, [new PeriodicCommand("char", TimeSpan.FromMinutes(1), TimeSpan.Zero)]);
        store.SetPeriodicCommands(serverScope, [new PeriodicCommand("server", TimeSpan.FromMinutes(1), TimeSpan.Zero)]);

        // Server scope known first (LoginComplete-equivalent).
        macro.StartServerScope(scheduler, serverScope);
        // Character scope resolves shortly after (SessionReady-equivalent),
        // before the 20-second timer's first evaluation.
        macro.SetCharacterScope(characterScope);

        macro.OnTimer();
        scheduler.Tick(0.1);
        scheduler.Tick(0.1);

        // §1.14: character-scope commands still queue before server-scope
        // ones within a single OnTimer evaluation.
        Assert.Equal(["char", "server"], host.Automation.Chat.Submitted);
    }

    [Fact]
    public void ABackfillAfterAnEarlyOnTimerStillEvaluatesTheCharacterScopeThisMinute()
    {
        // MEDIUM-B (P9 re-review): if OnTimer fires before SetCharacterScope
        // backfills (the character name resolved slower than the first
        // 20-second poll), _lastEvaluatedUtcMinute was already stamped for
        // this minute, so the character-scoped command would be skipped
        // until the minute rolled over -- not just delayed one poll, but
        // potentially up to a minute late (and missed outright for a
        // once-per-minute command whose window never repeats).
        (FakeHost host, PeriodicCommands macro, OpenAC.MagTools.TickScheduler scheduler,
            ScopedCommandStore store, FakeTimeProvider clock) = Build();

        clock.Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string characterScope = SettingsScope.Character("acct", "Server", "Acdream");
        string serverScope = SettingsScope.Server("Server");
        store.SetPeriodicCommands(characterScope, [new PeriodicCommand("char", TimeSpan.FromMinutes(1), TimeSpan.Zero)]);
        store.SetPeriodicCommands(serverScope, [new PeriodicCommand("server", TimeSpan.FromMinutes(1), TimeSpan.Zero)]);

        // Server scope known first; the timer's first poll lands BEFORE the
        // character scope backfills.
        macro.StartServerScope(scheduler, serverScope);
        macro.OnTimer();
        scheduler.Tick(0.1);

        // The character scope now resolves, still within the same UTC
        // minute as the poll above.
        macro.SetCharacterScope(characterScope);

        // The next 20-second poll (still minute 0) must re-evaluate and
        // catch the character-scoped command for THIS minute.
        clock.Now += TimeSpan.FromSeconds(20);
        macro.OnTimer();
        scheduler.Tick(0.1);
        scheduler.Tick(0.1);

        // ... and it must NOT re-dispatch the server-scoped command that
        // already matched this minute: the backfill re-arms only the
        // character scope.
        Assert.Equal(["server", "char"], host.Automation.Chat.Submitted);
        Assert.Equal(0, macro.PendingCount);
    }

    [Fact]
    public void WithNoCharacterScopeSetOnlyServerScopeCommandsFire()
    {
        (FakeHost host, PeriodicCommands macro, OpenAC.MagTools.TickScheduler scheduler,
            ScopedCommandStore store, FakeTimeProvider clock) = Build();

        clock.Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string characterScope = SettingsScope.Character("acct", "Server", "Acdream");
        string serverScope = SettingsScope.Server("Server");
        store.SetPeriodicCommands(characterScope, [new PeriodicCommand("char", TimeSpan.FromMinutes(1), TimeSpan.Zero)]);
        store.SetPeriodicCommands(serverScope, [new PeriodicCommand("server", TimeSpan.FromMinutes(1), TimeSpan.Zero)]);

        macro.StartServerScope(scheduler, serverScope);
        // Character scope never resolves this session.
        macro.OnTimer();
        scheduler.Tick(0.1);

        Assert.Equal(["server"], host.Automation.Chat.Submitted);
    }

    [Fact]
    public void StopDropsTheTimerAndAnyQueuedMatches()
    {
        (FakeHost host, PeriodicCommands macro, OpenAC.MagTools.TickScheduler scheduler,
            ScopedCommandStore store, FakeTimeProvider clock) = Build();

        clock.Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string scope = SettingsScope.Character("acct", "Server", "Acdream");
        store.SetPeriodicCommands(scope, [new PeriodicCommand("never runs", TimeSpan.FromMinutes(1), TimeSpan.Zero)]);
        macro.StartServerScope(scheduler, SettingsScope.Server("Server"));
        macro.SetCharacterScope(scope);
        macro.OnTimer();

        macro.Stop();
        scheduler.Tick(0.1);
        scheduler.Tick(20d);

        Assert.Empty(host.Automation.Chat.Submitted);
        Assert.Equal(0, macro.PendingCount);
    }

    [Fact]
    public void MinutesAfterMidnightUsesTheLocalWallClockNotUtc()
    {
        // M1: minutesAfterMidnight reads GetLocalNow() (the port's
        // DateTime.Now equivalent), not GetUtcNow(). A fixed +01:30 local
        // zone proves it: UTC 00:05 -> local 01:35 -> 95 minutes after local
        // midnight. interval=95/offset=0 only divides the LOCAL value
        // (95 % 95 == 0); the UTC value (5) would not (5 % 95 == 5).
        (FakeHost host, PeriodicCommands macro, OpenAC.MagTools.TickScheduler scheduler,
            ScopedCommandStore store, FakeTimeProvider clock) = Build();

        clock.Now = new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero);
        clock.LocalZone = TimeZoneInfo.CreateCustomTimeZone(
            "Test+0130", TimeSpan.FromMinutes(90), "Test+0130", "Test+0130");
        string scope = SettingsScope.Character("acct", "Server", "Acdream");
        store.SetPeriodicCommands(
            scope, [new PeriodicCommand("local wins", TimeSpan.FromMinutes(95), TimeSpan.Zero)]);

        macro.StartServerScope(scheduler, SettingsScope.Server("Server"));
        macro.SetCharacterScope(scope);
        macro.OnTimer();
        scheduler.Tick(0.1);

        Assert.Equal(["local wins"], host.Automation.Chat.Submitted);
    }

    [Fact]
    public void TheSameMinuteGuardStaysOnUtcRegardlessOfLocalZone()
    {
        // The guard is the original's own DateTime.UtcNow.Minute check, so a
        // non-UTC local zone must not affect whether a second poll within
        // the same UTC minute re-evaluates.
        (FakeHost host, PeriodicCommands macro, OpenAC.MagTools.TickScheduler scheduler,
            ScopedCommandStore store, FakeTimeProvider clock) = Build();

        clock.Now = new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero);
        clock.LocalZone = TimeZoneInfo.CreateCustomTimeZone(
            "Test+0130", TimeSpan.FromMinutes(90), "Test+0130", "Test+0130");
        string scope = SettingsScope.Character("acct", "Server", "Acdream");
        store.SetPeriodicCommands(
            scope, [new PeriodicCommand("say hi", TimeSpan.FromMinutes(95), TimeSpan.Zero)]);
        macro.StartServerScope(scheduler, SettingsScope.Server("Server"));
        macro.SetCharacterScope(scope);

        macro.OnTimer();
        clock.Now += TimeSpan.FromSeconds(20); // still 00:05 UTC minute
        macro.OnTimer();

        scheduler.Tick(0.1);
        scheduler.Tick(0.1);
        Assert.Single(host.Automation.Chat.Submitted);
    }

    [Fact]
    public void ZeroOrNegativeIntervalNeverMatches()
    {
        (FakeHost host, PeriodicCommands macro, OpenAC.MagTools.TickScheduler scheduler,
            ScopedCommandStore store, FakeTimeProvider clock) = Build();

        clock.Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string scope = SettingsScope.Character("acct", "Server", "Acdream");
        store.SetPeriodicCommands(scope, [new PeriodicCommand("never", TimeSpan.Zero, TimeSpan.Zero)]);
        macro.StartServerScope(scheduler, SettingsScope.Server("Server"));
        macro.SetCharacterScope(scope);

        macro.OnTimer();

        Assert.Equal(0, macro.PendingCount);
    }
}
