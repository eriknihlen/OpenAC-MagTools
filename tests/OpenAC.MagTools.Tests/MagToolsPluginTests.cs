using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Ui;

namespace OpenAC.MagTools.Tests;

public sealed class MagToolsPluginTests
{
    [Fact]
    public void EnableRegistersTheMtVerbAndBothPanels()
    {
        var host = new FakeHost { HasUi = true };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();

        Assert.True(host.Commands.Handlers.ContainsKey("mt"));
        Assert.Equal(2, host.Ui.Panels.Count);
        Assert.Equal(["main", "hud"],
            host.Ui.Panels.Select(panel => panel.Descriptor.WindowId));
        Assert.All(host.Ui.Panels, static panel =>
            Assert.False(panel.Descriptor.StartVisible));
        Assert.All(host.Ui.Panels, static panel =>
            Assert.True(panel.Descriptor.ShowInSidePanel));
        Assert.Equal("MT", host.Ui.Panels[0].Descriptor.IconText);
        Assert.IsType<MainViewModel>(host.Ui.Panels[0].Binding);
        Assert.IsType<HudViewModel>(host.Ui.Panels[1].Binding);
    }

    [Fact]
    public void AHeadlessHostGetsNoPanelsButStillGetsTheVerb()
    {
        var host = new FakeHost { HasUi = false };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();

        Assert.Empty(host.Ui.Panels);
        Assert.True(host.Commands.Handlers.ContainsKey("mt"));
    }

    [Fact]
    public void DisableUnhooksEverythingItRegistered()
    {
        var host = new FakeHost { HasUi = true };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();
        plugin.Disable();

        Assert.Empty(host.Commands.Handlers);
        Assert.Equal(0, host.Events.TickSubscriberCount);
        Assert.Empty(host.Ui.Panels);
    }

    [Fact]
    public void DisableAndReenableRefiresTheLoginEdge()
    {
        var host = new FakeHost { HasUi = false };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();

        host.Automation.IsAvailable = true;
        host.Automation.Character.IsInWorld = true;
        host.Events.RaiseTick(0.016d);

        plugin.Disable();
        plugin.Enable();
        host.Events.RaiseTick(0.016d);

        Assert.Equal(
            2,
            host.ChatLines.Count(line =>
                line == ChatOutput.Prefix + "Plugin now online."));
    }

    [Fact]
    public void DisableFlushesWhatTheSettingsDebounceStillOwes()
    {
        var host = new FakeHost { HasUi = false };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();

        host.Commands.Handlers["mt"](
            new AcDream.Plugin.Abstractions.PluginCommand(
                "mt", "opt set Filters.AttackEvades true", "/mt opt set Filters.AttackEvades true"));
        Assert.Equal(0, host.Storage.WriteCount);

        plugin.Disable();
        Assert.Equal(1, host.Storage.WriteCount);

        var reloaded = new SettingsManager(new SettingsFile(host.Storage));
        Assert.True(reloaded.Filters.AttackEvades.Value);
    }

    [Fact]
    public void TheTickPollsTheSessionEdge()
    {
        var host = new FakeHost { HasUi = false };
        var plugin = new MagToolsPlugin();

        plugin.Initialize(host);
        plugin.Enable();

        host.Automation.IsAvailable = true;
        host.Automation.Character.IsInWorld = true;
        host.Events.RaiseTick(0.016d);

        Assert.Contains(
            ChatOutput.Prefix + "Plugin now online.",
            host.ChatLines);
    }
}
