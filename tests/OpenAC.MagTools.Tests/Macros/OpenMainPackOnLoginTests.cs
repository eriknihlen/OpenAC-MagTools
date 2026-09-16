using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using Xunit;

namespace OpenAC.MagTools.Tests.Macros;

public sealed class OpenMainPackOnLoginTests
{
    [Fact]
    public void ShowsTheInventoryWindowWhenEnabled()
    {
        // A6: the mechanism moved from Items.Use(self) to the direct
        // client-window surface; same observable result (the pack opens).
        var host = new FakeHost { HasUi = true };
        var setting = new Setting<bool>(new SettingsFile(host.Storage), "Misc/OpenMainPackOnLogin", "Open Main Pack On Login", true);
        var macro = new OpenMainPackOnLogin(host, setting);

        macro.Run();

        Assert.Contains(PluginClientWindow.Inventory, host.Ui.ShowClientWindowCalls);
    }

    [Fact]
    public void DoesNothingWhenDisabled()
    {
        var host = new FakeHost { HasUi = true };
        var setting = new Setting<bool>(new SettingsFile(host.Storage), "Misc/OpenMainPackOnLogin", "Open Main Pack On Login", true);
        setting.Value = false;
        var macro = new OpenMainPackOnLogin(host, setting);

        macro.Run();

        Assert.Empty(host.Ui.ShowClientWindowCalls);
    }

    [Fact]
    public void DoesNothingOnANoWindowHost()
    {
        var host = new FakeHost { HasUi = false };
        var setting = new Setting<bool>(new SettingsFile(host.Storage), "Misc/OpenMainPackOnLogin", "Open Main Pack On Login", true);
        var macro = new OpenMainPackOnLogin(host, setting);

        macro.Run();

        Assert.Empty(host.Ui.ShowClientWindowCalls);
        Assert.Empty(((RecordingLogger)host.Log).Messages);
    }

    [Fact]
    public void LogsAWarningWhenTheHostRefusesTheWindow()
    {
        // The result must never be swallowed silently.
        var host = new FakeHost { HasUi = true };
        host.Ui.ShowClientWindowResult = false;
        var setting = new Setting<bool>(new SettingsFile(host.Storage), "Misc/OpenMainPackOnLogin", "Open Main Pack On Login", true);
        var macro = new OpenMainPackOnLogin(host, setting);

        macro.Run();

        Assert.Contains(((RecordingLogger)host.Log).Messages, m => m.StartsWith("warn:", StringComparison.Ordinal));
    }

    [Fact]
    public void DoesNotWarnWhenTheHostShowsTheWindow()
    {
        var host = new FakeHost { HasUi = true };
        var setting = new Setting<bool>(new SettingsFile(host.Storage), "Misc/OpenMainPackOnLogin", "Open Main Pack On Login", true);
        var macro = new OpenMainPackOnLogin(host, setting);

        macro.Run();

        Assert.Empty(((RecordingLogger)host.Log).Messages);
    }

    [Fact]
    public void DefaultsToEnabled()
    {
        // The original default: true. Confirms a freshly-constructed setting
        // (no stored value yet) still opens the pack.
        var host = new FakeHost { HasUi = true };
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var macro = new OpenMainPackOnLogin(host, settings.Misc.OpenMainPackOnLogin);

        macro.Run();

        Assert.Contains(PluginClientWindow.Inventory, host.Ui.ShowClientWindowCalls);
    }
}
