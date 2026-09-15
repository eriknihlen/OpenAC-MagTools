using OpenAC.MagTools.Tests.Fakes;

namespace OpenAC.MagTools.Tests;

public sealed class SessionContextTests
{
    private readonly FakeHost _host = new();
    private readonly SessionContext _session;
    private int _logins;
    private int _logoffs;

    public SessionContextTests()
    {
        _session = new SessionContext(_host, new ChatOutput(_host));
        _session.LoginComplete += () => _logins++;
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
