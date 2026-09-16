using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using Xunit;

namespace OpenAC.MagTools.Tests.Macros;

public sealed class AutoRechargeTests
{
    private static (FakeHost Host, SettingsManager Settings, OpenAC.MagTools.TickScheduler Scheduler, AutoRecharge AutoRecharge)
        Build()
    {
        var host = new FakeHost();
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, new OpenAC.MagTools.ChatOutput(host));
        var autoRecharge = new AutoRecharge(host, settings);
        autoRecharge.Start(scheduler);
        return (host, settings, scheduler, autoRecharge);
    }

    [Fact]
    public void A_low_on_mana_warning_starts_the_recharger()
    {
        var (host, _, _, autoRecharge) = Build();

        host.Automation.Chat.Deliver("Your Item of Wonder is low on Mana.");

        Assert.True(autoRecharge.Recharger.IsRunning);
    }

    [Fact]
    public void The_own_say_echo_is_ignored()
    {
        var (host, _, _, autoRecharge) = Build();

        host.Automation.Chat.Deliver("You say, \"Your item is low on Mana.\"");

        Assert.False(autoRecharge.Recharger.IsRunning);
    }

    [Fact]
    public void Another_players_says_line_is_ignored()
    {
        var (host, _, _, autoRecharge) = Build();

        host.Automation.Chat.Deliver("Bob says, \"Your item is low on Mana.\"");

        Assert.False(autoRecharge.Recharger.IsRunning);
    }

    [Fact]
    public void Disabled_by_setting_does_nothing()
    {
        var (host, settings, _, autoRecharge) = Build();
        settings.ManaManagement.AutoRecharge.Value = false;

        host.Automation.Chat.Deliver("Your Item of Wonder is low on Mana.");

        Assert.False(autoRecharge.Recharger.IsRunning);
    }

    [Fact]
    public void Stop_stops_the_recharger_too()
    {
        var (host, _, _, autoRecharge) = Build();
        host.Automation.Chat.Deliver("Your Item of Wonder is low on Mana.");
        Assert.True(autoRecharge.Recharger.IsRunning);

        autoRecharge.Stop();

        Assert.False(autoRecharge.Recharger.IsRunning);
    }
}
