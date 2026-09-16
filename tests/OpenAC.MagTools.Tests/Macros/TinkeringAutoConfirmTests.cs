using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Ui;

namespace OpenAC.MagTools.Tests.Macros;

public sealed class TinkeringAutoConfirmTests
{
    private static (FakeHost Host, TinkeringSettings Settings) Make()
    {
        var host = new FakeHost();
        var settingsFile = new OpenAC.MagTools.Settings.SettingsFile(host.Storage);
        return (host, new TinkeringSettings(settingsFile));
    }

    [Fact]
    public void ParsesTheRetailPercentSentence()
    {
        Assert.Equal(100, TinkeringAutoConfirm.TryParsePercent("You determine that you have a 100 percent chance to succeed."));
        Assert.Equal(87, TinkeringAutoConfirm.TryParsePercent("You determine that you have a 87 percent chance to succeed."));
        Assert.Null(TinkeringAutoConfirm.TryParsePercent("Some other message."));
    }

    [Fact]
    public void AnswersYesAtOrAboveTheFixedHundredPercentSetting()
    {
        (FakeHost host, TinkeringSettings settings) = Make();
        settings.AutoClickYes.Value = true;
        var confirm = new TinkeringAutoConfirm(host, settings);
        confirm.Start();

        host.Events.RaiseConfirmationRequested(new PluginConfirmation(7u, 5, "You determine that you have a 100 percent chance to succeed."));

        Assert.Single(host.Automation.Dialogs.Answers);
        Assert.Equal((7u, true), host.Automation.Dialogs.Answers[0]);
    }

    [Fact]
    public void DoesNotAnswerBelowTheThreshold()
    {
        (FakeHost host, TinkeringSettings settings) = Make();
        settings.AutoClickYes.Value = true;
        var confirm = new TinkeringAutoConfirm(host, settings);
        confirm.Start();

        host.Events.RaiseConfirmationRequested(new PluginConfirmation(7u, 5, "You determine that you have a 99 percent chance to succeed."));

        Assert.Empty(host.Automation.Dialogs.Answers);
    }

    [Fact]
    public void IgnoresConfirmationsOfAnotherType()
    {
        (FakeHost host, TinkeringSettings settings) = Make();
        settings.AutoClickYes.Value = true;
        var confirm = new TinkeringAutoConfirm(host, settings);
        confirm.Start();

        host.Events.RaiseConfirmationRequested(new PluginConfirmation(7u, 1, "You determine that you have a 100 percent chance to succeed."));

        Assert.Empty(host.Automation.Dialogs.Answers);
    }

    [Fact]
    public void TabThresholdCanArmALowerYesThanTheFixedSetting()
    {
        (FakeHost host, TinkeringSettings settings) = Make();
        settings.AutoClickYes.Value = false; // fixed watch disarmed
        var tab = new TinkeringPageViewModel();
        tab.SetMinimumPercent("80");
        var confirm = new TinkeringAutoConfirm(host, settings, tab);
        confirm.Start();

        host.Events.RaiseConfirmationRequested(new PluginConfirmation(9u, 5, "You determine that you have a 85 percent chance to succeed."));

        Assert.Single(host.Automation.Dialogs.Answers);
    }

    [Fact]
    public void StopUnsubscribes()
    {
        (FakeHost host, TinkeringSettings settings) = Make();
        settings.AutoClickYes.Value = true;
        var confirm = new TinkeringAutoConfirm(host, settings);
        confirm.Start();
        confirm.Stop();

        host.Events.RaiseConfirmationRequested(new PluginConfirmation(7u, 5, "You determine that you have a 100 percent chance to succeed."));

        Assert.Empty(host.Automation.Dialogs.Answers);
    }
}
