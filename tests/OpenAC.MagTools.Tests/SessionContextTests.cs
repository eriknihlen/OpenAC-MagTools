using OpenAC.MagTools.Tests.Fakes;

namespace OpenAC.MagTools.Tests;

public sealed class SessionContextTests
{
    private readonly FakeHost _host = new();
    private readonly SessionContext _session;
    private int _logins;
    private int _logoffs;
    private int _sessionReadies;

    public SessionContextTests()
    {
        _session = new SessionContext(_host, new ChatOutput(_host));
        _session.LoginComplete += () => _logins++;
        _session.SessionReady += () => _sessionReadies++;
        _session.Logoff += () => _logoffs++;
    }

    private void GoInWorld(string character = "+Acdream", int serverPopulation = -1)
    {
        _host.Automation.IsAvailable = true;
        _host.Automation.Character.IsInWorld = true;
        _host.Automation.Character.Name = character;
        _host.Automation.Character.WorldName = "Frostfell";
        _host.Automation.Character.AccountName = "testaccount";
        _host.Automation.Character.ServerPopulation = serverPopulation;
    }

    [Fact]
    public void NothingHappensBeforeTheSessionIsInWorld()
    {
        _session.Subscribe();

        Assert.False(_session.IsInWorld);
        Assert.Equal(0, _logins);
        Assert.Empty(_host.ChatLines);
    }

    [Fact]
    public void TheLoginCompleteEventRaisesLoginAndAnnouncesIt()
    {
        _session.Subscribe();
        GoInWorld();
        _host.Events.RaiseLoginComplete();

        Assert.True(_session.IsInWorld);
        Assert.Equal(1, _logins);
        Assert.Equal(
            ChatOutput.Prefix + "Plugin now online.",
            Assert.Single(_host.ChatLines));
    }

    [Fact]
    public void TheOnlineBannerIncludesServerPopulationWhenKnown()
    {
        _session.Subscribe();
        GoInWorld(serverPopulation: 213);
        _host.Events.RaiseLoginComplete();

        Assert.Equal(
            ChatOutput.Prefix + "Plugin now online. Server population: 213",
            Assert.Single(_host.ChatLines));
    }

    [Fact]
    public void SubscribingWhileAlreadyInWorldFiresLoginImmediately()
    {
        // Covers a Disable/Enable reload: the host will not raise
        // LoginComplete again for a session that never actually went away,
        // so Subscribe has to notice for itself.
        GoInWorld();

        _session.Subscribe();

        Assert.True(_session.IsInWorld);
        Assert.Equal(1, _logins);
    }

    [Fact]
    public void TheCharacterServerAndAccountAreCapturedAtLogin()
    {
        _session.Subscribe();
        GoInWorld();
        _host.Events.RaiseLoginComplete();

        Assert.Equal("+Acdream", _session.CharacterName);
        Assert.Equal("Frostfell", _session.WorldName);
        Assert.Equal("testaccount", _session.AccountName);
    }

    [Fact]
    public void LosingTheSessionRaisesLogoffOnceAndClearsTheIdentity()
    {
        _session.Subscribe();
        GoInWorld();
        _host.Events.RaiseLoginComplete();

        _host.Automation.Character.IsInWorld = false;
        _host.Events.RaiseLogoff();

        Assert.False(_session.IsInWorld);
        Assert.Equal(1, _logoffs);
        Assert.Equal(string.Empty, _session.CharacterName);
    }

    [Fact]
    public void AReconnectLooksLikeAFreshLogin()
    {
        // A generation reset does not recycle the plugin, so the same context
        // has to see login, logoff and login again.
        _session.Subscribe();
        GoInWorld();
        _host.Events.RaiseLoginComplete();

        _host.Automation.IsAvailable = false;
        _host.Events.RaiseLogoff();

        GoInWorld("+Horan");
        _host.Events.RaiseLoginComplete();

        Assert.Equal(2, _logins);
        Assert.Equal(1, _logoffs);
        Assert.Equal("+Horan", _session.CharacterName);
    }

    [Fact]
    public void TheLogoffIdentityIsStillReadableFromTheHandler()
    {
        string seen = string.Empty;
        _session.Logoff += () => seen = _session.CharacterName;

        _session.Subscribe();
        GoInWorld();
        _host.Events.RaiseLoginComplete();
        _host.Automation.Character.IsInWorld = false;
        _host.Events.RaiseLogoff();

        Assert.Equal("+Acdream", seen);
    }

    [Fact]
    public void SessionReadyFiresImmediatelyWhenNamesAreAlreadyKnown()
    {
        _session.Subscribe();
        GoInWorld();
        _host.Events.RaiseLoginComplete();

        Assert.Equal(1, _sessionReadies);
        Assert.Equal("+Acdream", _session.CharacterName);
    }

    [Fact]
    public void SessionReadyWaitsUntilTheNamesResolveOnALaterTick()
    {
        // The host defect this guards: ICharacterInfo.Name (and friends) can
        // still be empty on the exact tick LoginComplete fires — the player
        // object exists in the object table before its name arrives. Every
        // name-scoped owner must wait for SessionReady, not LoginComplete.
        _session.Subscribe();

        _host.Automation.IsAvailable = true;
        _host.Automation.Character.IsInWorld = true;
        _host.Automation.Character.Name = string.Empty;
        _host.Automation.Character.WorldName = string.Empty;
        _host.Automation.Character.AccountName = string.Empty;
        _host.Events.RaiseLoginComplete();

        Assert.True(_session.IsInWorld);
        Assert.Equal(1, _logins);
        Assert.Equal(0, _sessionReadies);
        Assert.Equal(string.Empty, _session.CharacterName);

        // Tick 1: still empty.
        _host.Events.RaiseTick(0.1);
        Assert.Equal(0, _sessionReadies);

        // Tick 2: the host has now populated the name.
        _host.Automation.Character.Name = "+Acdream";
        _host.Automation.Character.WorldName = "Frostfell";
        _host.Automation.Character.AccountName = "testaccount";
        _host.Events.RaiseTick(0.1);

        Assert.Equal(1, _sessionReadies);
        Assert.Equal("+Acdream", _session.CharacterName);
        Assert.Equal("Frostfell", _session.WorldName);
        Assert.Equal("testaccount", _session.AccountName);

        // The tick subscription is dropped once resolved, so a further tick
        // does not re-raise SessionReady.
        _host.Events.RaiseTick(0.1);
        Assert.Equal(1, _sessionReadies);
    }

    [Fact]
    public void SessionReadyNeverFiresIfTheNameNeverArrivesBeforeLogoff()
    {
        _session.Subscribe();

        _host.Automation.IsAvailable = true;
        _host.Automation.Character.IsInWorld = true;
        _host.Automation.Character.Name = string.Empty;
        _host.Automation.Character.WorldName = string.Empty;
        _host.Automation.Character.AccountName = string.Empty;
        _host.Events.RaiseLoginComplete();

        _host.Events.RaiseTick(0.1);
        _host.Events.RaiseTick(0.1);

        Assert.Equal(0, _sessionReadies);

        _host.Automation.Character.IsInWorld = false;
        _host.Events.RaiseLogoff();

        Assert.Equal(0, _sessionReadies);
        Assert.Equal(1, _logoffs);
        Assert.Contains(
            ((RecordingLogger)_host.Log).Messages,
            message => message.StartsWith("warn: ", StringComparison.Ordinal));

        // A further tick after logoff must not resurrect the check (the
        // Tick subscription was dropped at Logoff).
        _host.Events.RaiseTick(0.1);
        Assert.Equal(0, _sessionReadies);
    }

    [Fact]
    public void AReconnectWithAnEmptyNameStillResolvesAndReRaisesSessionReady()
    {
        // A reconnect (Logoff then a fresh LoginComplete) must re-arm name
        // resolution from scratch -- it must not stay stuck on whatever the
        // FIRST session's resolution state was.
        _session.Subscribe();
        GoInWorld();
        _host.Events.RaiseLoginComplete();
        Assert.Equal(1, _sessionReadies);

        _host.Automation.IsAvailable = false;
        _host.Events.RaiseLogoff();

        // The reconnected session's name is empty at first, exactly like the
        // first session's host defect.
        _host.Automation.IsAvailable = true;
        _host.Automation.Character.IsInWorld = true;
        _host.Automation.Character.Name = string.Empty;
        _host.Automation.Character.WorldName = string.Empty;
        _host.Automation.Character.AccountName = string.Empty;
        _host.Events.RaiseLoginComplete();

        Assert.Equal(2, _logins);
        // The name is still empty on this reconnected session -- SessionReady
        // must not fire again yet (still 1, from the first session).
        Assert.Equal(1, _sessionReadies);

        _host.Events.RaiseTick(0.1);
        Assert.Equal(1, _sessionReadies); // still pending after one tick

        _host.Automation.Character.Name = "+Horan";
        _host.Automation.Character.WorldName = "Frostfell";
        _host.Automation.Character.AccountName = "testaccount";
        _host.Events.RaiseTick(0.1);

        Assert.Equal(2, _sessionReadies);
        Assert.Equal("+Horan", _session.CharacterName);
    }

    [Fact]
    public void UnsubscribeDuringPendingResolutionStopsTheTickSubscription()
    {
        // If Disable() (Unsubscribe) runs while a name resolution is still
        // pending, the Tick subscription it armed must be dropped -- an
        // Unsubscribe must not leave a stray Tick handler running against a
        // session the plugin no longer considers itself attached to.
        _session.Subscribe();

        _host.Automation.IsAvailable = true;
        _host.Automation.Character.IsInWorld = true;
        _host.Automation.Character.Name = string.Empty;
        _host.Automation.Character.WorldName = string.Empty;
        _host.Automation.Character.AccountName = string.Empty;
        _host.Events.RaiseLoginComplete();

        Assert.Equal(0, _sessionReadies);

        _session.Unsubscribe();

        // Even though the name resolves after Unsubscribe, no SessionReady
        // should fire -- the tick handler was dropped.
        _host.Automation.Character.Name = "+Acdream";
        _host.Automation.Character.WorldName = "Frostfell";
        _host.Automation.Character.AccountName = "testaccount";
        _host.Events.RaiseTick(0.1);

        Assert.Equal(0, _sessionReadies);
    }

    [Fact]
    public void UnsubscribeStopsReactingToTheHostEvents()
    {
        _session.Subscribe();
        _session.Unsubscribe();

        GoInWorld();
        _host.Events.RaiseLoginComplete();

        Assert.False(_session.IsInWorld);
        Assert.Equal(0, _logins);
    }
}
