using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using Xunit;

namespace OpenAC.MagTools.Tests.Macros;

public sealed class LogOutOnDeathTests
{
    private static (FakeHost Host, Setting<bool> Setting, LogOutOnDeath Macro) Build()
    {
        var host = new FakeHost();
        var setting = new Setting<bool>(new SettingsFile(host.Storage), "Misc/LogOutOnDeath", "Log Out on Death");
        var macro = new LogOutOnDeath(host, setting);
        return (host, setting, macro);
    }

    [Fact]
    public void LogsOutWhenEnabledAndThePlayerDies()
    {
        (FakeHost host, Setting<bool> setting, LogOutOnDeath macro) = Build();
        setting.Value = true;
        macro.Start();

        host.Events.RaiseLocalPlayerDied("You have died.");

        Assert.Equal(1, host.Automation.Login.LogoutCalls);
        Assert.Equal(1, macro.LogoutAttempts);
    }

    [Fact]
    public void DefaultsToDisabledAndNeverLogsOut()
    {
        (FakeHost host, _, LogOutOnDeath macro) = Build();
        macro.Start();

        host.Events.RaiseLocalPlayerDied("You have died.");

        Assert.Equal(0, host.Automation.Login.LogoutCalls);
    }

    [Fact]
    public void DoesNothingAfterStop()
    {
        (FakeHost host, Setting<bool> setting, LogOutOnDeath macro) = Build();
        setting.Value = true;
        macro.Start();
        macro.Stop();

        host.Events.RaiseLocalPlayerDied("You have died.");

        Assert.Equal(0, host.Automation.Login.LogoutCalls);
    }

    [Fact]
    public void StartIsIdempotent()
    {
        (FakeHost host, Setting<bool> setting, LogOutOnDeath macro) = Build();
        setting.Value = true;
        macro.Start();
        macro.Start();

        host.Events.RaiseLocalPlayerDied("You have died.");

        // A double Start() must not double-subscribe.
        Assert.Equal(1, host.Automation.Login.LogoutCalls);
    }
}
