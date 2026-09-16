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
    public void RunsTheDummySlotFirstThenOneCommandPerTick()
    {
        (FakeHost host, LoginActions actions, OpenAC.MagTools.TickScheduler scheduler, ScopedCommandStore store) = Build();
        string characterScope = SettingsScope.Character("acct", "Server", "Acdream");
        string serverScope = SettingsScope.Server("Server");
        store.SetOnLoginCommands(characterScope, ["/mt opt list"]);

        actions.Run(scheduler, characterScope, serverScope);

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
    public void DispatchesMtCommandsThroughTheRouterAndEverythingElseToChat()
    {
        (FakeHost host, LoginActions actions, OpenAC.MagTools.TickScheduler scheduler, ScopedCommandStore store) = Build();
        string characterScope = SettingsScope.Character("acct", "Server", "Acdream");
        string serverScope = SettingsScope.Server("Server");
        store.SetOnLoginCommands(characterScope, ["/mt test", "hello there"]);

        actions.Run(scheduler, characterScope, serverScope);

        scheduler.Tick(0.1); // dummy
        scheduler.Tick(0.1); // "/mt test" -> router.Execute("test") is a recognised no-op returning true
        scheduler.Tick(0.1); // "hello there" -> chat submit

        Assert.Contains("hello there", host.Automation.Chat.Submitted);
    }

    [Fact]
    public void CharacterCommandsRunBeforeServerCommands()
    {
        (FakeHost host, LoginActions actions, OpenAC.MagTools.TickScheduler scheduler, ScopedCommandStore store) = Build();
        string characterScope = SettingsScope.Character("acct", "Server", "Acdream");
        string serverScope = SettingsScope.Server("Server");
        store.SetOnLoginCommands(characterScope, ["char one"]);
        store.SetOnLoginCommands(serverScope, ["server one"]);

        actions.Run(scheduler, characterScope, serverScope);

        scheduler.Tick(0.1); // dummy
        scheduler.Tick(0.1); // char one
        scheduler.Tick(0.1); // server one

        Assert.Equal(["char one", "server one"], host.Automation.Chat.Submitted);
    }

    [Fact]
    public void OnLoginRunsBeforeOnLoginCompleteOnTheSameEdge()
    {
        (FakeHost host, LoginActions actions, OpenAC.MagTools.TickScheduler scheduler, ScopedCommandStore store) = Build();
        string characterScope = SettingsScope.Character("acct", "Server", "Acdream");
        string serverScope = SettingsScope.Server("Server");
        store.SetOnLoginCommands(characterScope, ["login command"]);
        store.SetOnLoginCompleteCommands(characterScope, ["login complete command"]);

        actions.Run(scheduler, characterScope, serverScope);

        scheduler.Tick(0.1); // dummy
        scheduler.Tick(0.1); // login command
        scheduler.Tick(0.1); // login complete command

        Assert.Equal(["login command", "login complete command"], host.Automation.Chat.Submitted);
    }

    [Fact]
    public void StopDropsWhateverIsStillQueued()
    {
        (FakeHost host, LoginActions actions, OpenAC.MagTools.TickScheduler scheduler, ScopedCommandStore store) = Build();
        string characterScope = SettingsScope.Character("acct", "Server", "Acdream");
        string serverScope = SettingsScope.Server("Server");
        store.SetOnLoginCommands(characterScope, ["never runs"]);

        actions.Run(scheduler, characterScope, serverScope);
        actions.Stop();

        scheduler.Tick(0.1);
        scheduler.Tick(0.1);
        scheduler.Tick(0.1);

        Assert.Empty(host.Automation.Chat.Submitted);
        Assert.Equal(0, actions.PendingCount);
    }

    [Fact]
    public void ARepeatLoginEdgeStartsAFreshQueue()
    {
        (FakeHost host, LoginActions actions, OpenAC.MagTools.TickScheduler scheduler, ScopedCommandStore store) = Build();
        string characterScope = SettingsScope.Character("acct", "Server", "Acdream");
        string serverScope = SettingsScope.Server("Server");
        store.SetOnLoginCommands(characterScope, ["first login command"]);

        actions.Run(scheduler, characterScope, serverScope);
        // Reconnect before the first queue drained.
        actions.Run(scheduler, characterScope, serverScope);

        scheduler.Tick(0.1); // dummy
        scheduler.Tick(0.1); // first login command (only once, not duplicated)
        scheduler.Tick(0.1); // nothing left

        Assert.Equal(["first login command"], host.Automation.Chat.Submitted);
    }
}
