using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Commands;
using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using Xunit;

namespace OpenAC.MagTools.Tests.Macros;

public sealed class LoginActionsTests
{
    private static (FakeHost Host, LoginActions Actions, OpenAC.MagTools.TickScheduler Scheduler, ScopedCommandStore Store)
        Build()
    {
        var host = new FakeHost();
        var chat = new ChatOutput(host);
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var router = new MtCommandRouter(host, chat, settings);
        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, chat);
        var actions = new LoginActions(host, router, settings.Commands);
        return (host, actions, scheduler, settings.Commands);
    }

    [Fact]
    public void ServerScopeRunsTheDummySlotFirstThenOneCommandPerTick()
    {
        (FakeHost host, LoginActions actions, OpenAC.MagTools.TickScheduler scheduler, ScopedCommandStore store) = Build();
        string serverScope = SettingsScope.Server("Server");
        store.SetOnLoginCommands(serverScope, ["/mt opt list"]);

        actions.RunServerScope(scheduler, serverScope);

        // The dummy slot occupies the very first tick: nothing dispatched yet.
        scheduler.Tick(0.1);
        Assert.Empty(host.ChatLines);
        Assert.Equal(1, actions.PendingCount);

        // The real command dispatches on the SECOND tick.
        scheduler.Tick(0.1);
        Assert.DoesNotContain(
            "<{Mag-Tools}>: Usage: /mt <command>. Try /mt opt list.", host.ChatLines);
        Assert.Equal(0, actions.PendingCount);
    }

    [Fact]
    public void CharacterScopeRunsTheDummySlotFirstThenOneCommandPerTick()
    {
        (FakeHost host, LoginActions actions, OpenAC.MagTools.TickScheduler scheduler, ScopedCommandStore store) = Build();
        string characterScope = SettingsScope.Character("acct", "Server", "Acdream");
        store.SetOnLoginCommands(characterScope, ["/mt opt list"]);

        actions.RunCharacterScope(scheduler, characterScope);

        scheduler.Tick(0.1);
        Assert.Empty(host.ChatLines);
        Assert.Equal(1, actions.PendingCount);

        scheduler.Tick(0.1);
        Assert.Equal(0, actions.PendingCount);
    }

    [Fact]
    public void DispatchesMtCommandsThroughTheRouterAndEverythingElseToChat()
    {
        (FakeHost host, LoginActions actions, OpenAC.MagTools.TickScheduler scheduler, ScopedCommandStore store) = Build();
        string characterScope = SettingsScope.Character("acct", "Server", "Acdream");
        store.SetOnLoginCommands(characterScope, ["/mt test", "hello there"]);

        actions.RunCharacterScope(scheduler, characterScope);

        scheduler.Tick(0.1); // dummy
        scheduler.Tick(0.1); // "/mt test" -> router.Execute("test") is a recognised no-op returning true
        scheduler.Tick(0.1); // "hello there" -> chat submit

        Assert.Contains("hello there", host.Automation.Chat.Submitted);
    }

    [Fact]
    public void TheTwoScopesRunOnIndependentQueuesAndCanOverlap()
    {
        // MEDIUM-4: server scope starts at LoginComplete (RunServerScope),
        // character scope only once SessionReady confirms the name
        // (RunCharacterScope) -- they no longer share one combined queue, so
        // both can be mid-drain at once instead of strictly serializing
        // character-before-server.
        (FakeHost host, LoginActions actions, OpenAC.MagTools.TickScheduler scheduler, ScopedCommandStore store) = Build();
        string characterScope = SettingsScope.Character("acct", "Server", "Acdream");
        string serverScope = SettingsScope.Server("Server");
        store.SetOnLoginCommands(characterScope, ["char one"]);
        store.SetOnLoginCommands(serverScope, ["server one"]);

        actions.RunServerScope(scheduler, serverScope);
        actions.RunCharacterScope(scheduler, characterScope);

        scheduler.Tick(0.1); // both dummies
        scheduler.Tick(0.1); // both real commands land on the same tick

        Assert.Equal(2, host.Automation.Chat.Submitted.Count);
        Assert.Contains("char one", host.Automation.Chat.Submitted);
        Assert.Contains("server one", host.Automation.Chat.Submitted);
    }

    [Fact]
    public void OnLoginRunsBeforeOnLoginCompleteOnTheSameScope()
    {
        (FakeHost host, LoginActions actions, OpenAC.MagTools.TickScheduler scheduler, ScopedCommandStore store) = Build();
        string characterScope = SettingsScope.Character("acct", "Server", "Acdream");
        store.SetOnLoginCommands(characterScope, ["login command"]);
        store.SetOnLoginCompleteCommands(characterScope, ["login complete command"]);

        actions.RunCharacterScope(scheduler, characterScope);

        scheduler.Tick(0.1); // dummy
        scheduler.Tick(0.1); // login command
        scheduler.Tick(0.1); // login complete command

        Assert.Equal(["login command", "login complete command"], host.Automation.Chat.Submitted);
    }

    [Fact]
    public void StopDropsWhateverIsStillQueuedInBothScopes()
    {
        (FakeHost host, LoginActions actions, OpenAC.MagTools.TickScheduler scheduler, ScopedCommandStore store) = Build();
        string characterScope = SettingsScope.Character("acct", "Server", "Acdream");
        string serverScope = SettingsScope.Server("Server");
        store.SetOnLoginCommands(characterScope, ["never runs (character)"]);
        store.SetOnLoginCommands(serverScope, ["never runs (server)"]);

        actions.RunServerScope(scheduler, serverScope);
        actions.RunCharacterScope(scheduler, characterScope);
        actions.Stop();

        scheduler.Tick(0.1);
        scheduler.Tick(0.1);
        scheduler.Tick(0.1);

        Assert.Empty(host.Automation.Chat.Submitted);
        Assert.Equal(0, actions.PendingCount);
    }

    [Fact]
    public void ARepeatServerScopeEdgeStartsAFreshQueue()
    {
        (FakeHost host, LoginActions actions, OpenAC.MagTools.TickScheduler scheduler, ScopedCommandStore store) = Build();
        string serverScope = SettingsScope.Server("Server");
        store.SetOnLoginCommands(serverScope, ["first login command"]);

        actions.RunServerScope(scheduler, serverScope);
        // Reconnect before the first queue drained.
        actions.RunServerScope(scheduler, serverScope);

        scheduler.Tick(0.1); // dummy
        scheduler.Tick(0.1); // first login command (only once, not duplicated)
        scheduler.Tick(0.1); // nothing left

        Assert.Equal(["first login command"], host.Automation.Chat.Submitted);
    }

    [Fact]
    public void ARepeatCharacterScopeEdgeStartsAFreshQueue()
    {
        (FakeHost host, LoginActions actions, OpenAC.MagTools.TickScheduler scheduler, ScopedCommandStore store) = Build();
        string characterScope = SettingsScope.Character("acct", "Server", "Acdream");
        store.SetOnLoginCommands(characterScope, ["first login command"]);

        actions.RunCharacterScope(scheduler, characterScope);
        actions.RunCharacterScope(scheduler, characterScope);

        scheduler.Tick(0.1); // dummy
        scheduler.Tick(0.1); // first login command (only once, not duplicated)
        scheduler.Tick(0.1); // nothing left

        Assert.Equal(["first login command"], host.Automation.Chat.Submitted);
    }
}
