using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using Xunit;

namespace OpenAC.MagTools.Tests.Macros;

public sealed class OpenMainPackOnLoginTests
{
    [Fact]
    public void UsesTheLocalPlayersOwnObjectIdWhenEnabled()
    {
        var host = new FakeHost();
        host.Automation.Character.ObjectId = 555u;
        var setting = new Setting<bool>(new SettingsFile(host.Storage), "Misc/OpenMainPackOnLogin", "Open Main Pack On Login", true);
        var macro = new OpenMainPackOnLogin(host, setting);

        macro.Run();

        Assert.Contains(("use", 555u, 0u), host.Automation.Items.Calls);
    }

    [Fact]
    public void DoesNothingWhenDisabled()
    {
        var host = new FakeHost();
        host.Automation.Character.ObjectId = 555u;
        var setting = new Setting<bool>(new SettingsFile(host.Storage), "Misc/OpenMainPackOnLogin", "Open Main Pack On Login", true);
        setting.Value = false;
        var macro = new OpenMainPackOnLogin(host, setting);

        macro.Run();

        Assert.Empty(host.Automation.Items.Calls);
    }

    [Fact]
    public void DefaultsToEnabled()
    {
        // The original default: true. Confirms a freshly-constructed setting
        // (no stored value yet) still opens the pack.
        var host = new FakeHost();
        host.Automation.Character.ObjectId = 1u;
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var macro = new OpenMainPackOnLogin(host, settings.Misc.OpenMainPackOnLogin);

        macro.Run();

        Assert.Contains(("use", 1u, 0u), host.Automation.Items.Calls);
    }
}
