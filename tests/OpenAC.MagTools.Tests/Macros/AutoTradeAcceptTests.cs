using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Tests.Trackers.Combat;
using Xunit;

namespace OpenAC.MagTools.Tests.Macros;

public sealed class AutoTradeAcceptTests
{
    private static (FakeHost Host, AutoTradeAcceptSettings Settings, FakeTimeProvider Clock) Build()
    {
        var host = new FakeHost();
        var settings = new AutoTradeAcceptSettings(new SettingsFile(host.Storage));
        settings.Enabled.Value = true;
        var clock = new FakeTimeProvider();
        var macro = new AutoTradeAccept(host, settings, clock);
        macro.Start();
        return (host, settings, clock);
    }

    [Fact]
    public void AcceptsWhenThePartnersNameMatchesAWhitelistPattern()
    {
        (FakeHost host, AutoTradeAcceptSettings settings, _) = Build();
        settings.SetWhitelistPatterns(["Bob.*"]);
        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(2u, "Bob the Mule", PluginObjectClass.Player, 0));

        host.Automation.Trade.RaisePartnerAccepted(2u);

        Assert.Contains("accept", host.Automation.Trade.Calls);
    }

    [Fact]
    public void DoesNotAcceptWhenTheNameDoesNotMatch()
    {
        var host = new FakeHost();
        var settings = new AutoTradeAcceptSettings(new SettingsFile(host.Storage));
        settings.Enabled.Value = true;
        settings.SetWhitelistPatterns(["Bob.*"]);
        var macro = new AutoTradeAccept(host, settings);
        macro.Start();

        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(2u, "Random Stranger", PluginObjectClass.Player, 0));
        host.Automation.Trade.RaisePartnerAccepted(2u);

        Assert.Empty(host.Automation.Trade.Calls);
    }

    // Defect 13 (live-gate round 4): confirmed root cause is the headless
    // host's Automation.Objects falling through to the shared
    // NoOpAutomationSurface (HeadlessAutomationSurface under OpenAcRoot only
    // overrides Chat/Login/Dialogs/Trade/Vendor), whose TryGet always
    // returns false -- so a headless bot's AutoTradeAccept can NEVER resolve
    // the partner's name and NEVER reaches the whitelist match or Accept().
    // This locks in that the method fails SAFELY (no exception, no accept)
    // when the partner cannot be resolved, matching that host shape, rather
    // than asserting a fix belongs here -- the gap is in
    // AcDream.Headless.Plugins.HeadlessAutomationSurface, not this class.
    [Fact]
    public void DoesNotAcceptWhenThePartnerCannotBeResolved()
    {
        (FakeHost host, AutoTradeAcceptSettings settings, _) = Build();
        settings.SetWhitelistPatterns([".*"]);
        // No corresponding FakeObjects entry -- TryGet returns false, the
        // same shape the headless host's NoOp Objects surface always
        // produces for every plugin, not just this one.

        host.Automation.Trade.RaisePartnerAccepted(2u);

        Assert.Empty(host.Automation.Trade.Calls);
    }

    [Fact]
    public void DisabledSettingNeverAccepts()
    {
        var host = new FakeHost();
        var settings = new AutoTradeAcceptSettings(new SettingsFile(host.Storage));
        settings.SetWhitelistPatterns([".*"]);
        var macro = new AutoTradeAccept(host, settings);
        macro.Start();

        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(2u, "Anyone", PluginObjectClass.Player, 0));
        host.Automation.Trade.RaisePartnerAccepted(2u);

        Assert.Empty(host.Automation.Trade.Calls);
    }

    [Fact]
    public void RateLimitsToOneAcceptPerTwoSeconds()
    {
        var host = new FakeHost();
        var settings = new AutoTradeAcceptSettings(new SettingsFile(host.Storage));
        settings.Enabled.Value = true;
        settings.SetWhitelistPatterns([".*"]);
        var clock = new FakeTimeProvider();
        var macro = new AutoTradeAccept(host, settings, clock);
        macro.Start();

        host.Automation.Objects.Objects.Add(FakeObjects.Landscape(2u, "Bob", PluginObjectClass.Player, 0));

        host.Automation.Trade.RaisePartnerAccepted(2u);
        host.Automation.Trade.RaisePartnerAccepted(2u);
        Assert.Single(host.Automation.Trade.Calls);

        clock.Advance(TimeSpan.FromSeconds(2.1));
        host.Automation.Trade.RaisePartnerAccepted(2u);
        Assert.Equal(2, host.Automation.Trade.Calls.Count);
    }
}
