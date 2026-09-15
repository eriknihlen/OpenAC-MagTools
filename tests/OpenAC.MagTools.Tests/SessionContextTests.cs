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

    private void GoInWorld(string character = "+Acdream")
    {
        _host.Automation.IsAvailable = true;
        _host.Automation.Character.IsInWorld = true;
        _host.Automation.Character.Name = character;
        _host.Automation.Character.WorldName = "Frostfell";
        _host.Automation.Character.AccountName = "testaccount";
    }

    [Fact]
    public void NothingHappensBeforeTheSessionIsInWorld()
    {
        _session.Poll();
        _session.Poll();

        Assert.False(_session.IsInWorld);
        Assert.Equal(0, _logins);
        Assert.Empty(_host.ChatLines);
    }

    [Fact]
    public void GoingInWorldRaisesLoginOnceAndAnnouncesIt()
    {
        GoInWorld();
        _session.Poll();
        _session.Poll();

        Assert.True(_session.IsInWorld);
        Assert.Equal(1, _logins);
        Assert.Equal(
            ChatOutput.Prefix + "Plugin now online.",
            Assert.Single(_host.ChatLines));
    }

    [Fact]
    public void TheCharacterServerAndAccountAreCapturedAtLogin()
    {
        GoInWorld();
        _session.Poll();

        Assert.Equal("+Acdream", _session.CharacterName);
        Assert.Equal("Frostfell", _session.WorldName);
        Assert.Equal("testaccount", _session.AccountName);
    }

    [Fact]
    public void LosingTheSessionRaisesLogoffOnceAndClearsTheIdentity()
    {
        GoInWorld();
        _session.Poll();

        _host.Automation.Character.IsInWorld = false;
        _session.Poll();
        _session.Poll();

        Assert.False(_session.IsInWorld);
        Assert.Equal(1, _logoffs);
        Assert.Equal(string.Empty, _session.CharacterName);
    }

    [Fact]
    public void TheAutomationSurfaceGoingAwayCountsAsALogoff()
    {
        GoInWorld();
        _session.Poll();

        _host.Automation.IsAvailable = false;
        _session.Poll();

        Assert.False(_session.IsInWorld);
        Assert.Equal(1, _logoffs);
    }

    [Fact]
    public void AReconnectLooksLikeAFreshLogin()
    {
        // A generation reset does not recycle the plugin, so the same context
        // has to see login, logoff and login again.
        GoInWorld();
        _session.Poll();

        _host.Automation.IsAvailable = false;
        _session.Poll();

        GoInWorld("+Horan");
        _session.Poll();

        Assert.Equal(2, _logins);
        Assert.Equal(1, _logoffs);
        Assert.Equal("+Horan", _session.CharacterName);
    }

    [Fact]
    public void TheLogoffIdentityIsStillReadableFromTheHandler()
    {
        string seen = string.Empty;
        _session.Logoff += () => seen = _session.CharacterName;

        GoInWorld();
        _session.Poll();
        _host.Automation.Character.IsInWorld = false;
        _session.Poll();

        Assert.Equal("+Acdream", seen);
    }
}
