using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Macros;

/// <summary>
/// Port of the original's §1.7 <c>AutoTradeAccept</c>: when the trade partner
/// accepts and their name matches a whitelist regex, accept the trade back.
/// Setting: <c>AutoTradeAccept/Enabled</c> (default false); the whitelist
/// itself is <c>AutoTradeAccept/Whitelist</c>, hand-edited XML with no GUI
/// (each child's inner text wrapped <c>"^" + text + "$"</c>), same as the
/// original.
/// </summary>
/// <remarks>
/// The original's <c>WorldFilter.AcceptTrade(TargetId)</c> fired for EITHER
/// side accepting and explicitly ignored the case where <c>TargetId</c> was
/// the local player. <see cref="ITradeAutomation.PartnerTradeAccepted"/>
/// already only fires for the partner's own acceptance, so that ignore check
/// has nothing left to do here — see docs/deviations.md. The 2-second
/// rate limit ("avoid double spamming it from our own TradeAccept()") is
/// preserved verbatim.
/// </remarks>
/// <remarks>
/// Defect 13 root cause (live-gate round 4, headless <c>+Horan</c> bot never
/// accepted): confirmed HOST-side, not this class. Every
/// <see cref="IPluginHost.Automation"/> member this method depends on --
/// <see cref="IAutomationSurface.Objects"/> in particular -- falls through
/// to <c>IAutomationSurface</c>'s own interface default
/// (<c>NoOpAutomationSurface.Instance</c>) on the headless host, because
/// <c>AcDream.Headless.Plugins.HeadlessAutomationSurface</c> (under
/// <c>OpenAcRoot</c>) only overrides <c>Chat</c>/<c>Login</c>/
/// <c>Dialogs</c>/<c>Trade</c>/<c>Vendor</c> -- <c>Character</c>,
/// <c>Items</c>, <c>Objects</c>, <c>Loot</c>, <c>Navigation</c>, etc. are all
/// left at that shared no-op sink, whose <c>TryGet</c> unconditionally
/// returns <see langword="false"/>. So <see cref="OnPartnerAccepted"/>'s
/// <c>Objects.TryGet(partnerObjectId, ...)</c> call below can NEVER succeed
/// on headless, regardless of whitelist content, rate-limit state, or
/// timing -- it bails before ever reaching the whitelist match or
/// <see cref="ITradeAutomation.Accept"/>. This is NOT the "empty
/// <c>Character.Name</c> at <c>SessionReady</c>" pattern seen elsewhere in
/// this port (this method never reads <c>Character</c> at all); it is a
/// separate, broader gap -- headless has no working world-object lookup for
/// ANY plugin at all yet. The whitelist match and the
/// <see cref="ITradeAutomation.Accept"/> call below are otherwise correct
/// and require no plugin-side change once the host wires a real
/// <c>Objects</c> implementation into <c>HeadlessAutomationSurface</c> (the
/// same way <c>Trade</c>/<c>Vendor</c> already got one).
/// </remarks>
public sealed class AutoTradeAccept
{
    private static readonly TimeSpan RateLimit = TimeSpan.FromSeconds(2);

    private readonly IPluginHost _host;
    private readonly AutoTradeAcceptSettings _settings;
    private readonly TimeProvider _timeProvider;

    private Action<uint>? _onPartnerAccepted;

    /// <summary>
    /// LOW: stamped only at the point <see cref="ITradeAutomation.Accept"/>
    /// is actually SENT (a whitelist match), not on every
    /// <see cref="ITradeAutomation.PartnerTradeAccepted"/> event this macro
    /// observes. A non-matching partner accepting, or Enabled being false,
    /// never consumes the rate-limit window -- the 2-second guard exists
    /// purely to stop OUR OWN `Accept()` call's own acceptance echo from
    /// re-triggering this handler, not to throttle how often partners may
    /// accept.
    /// </summary>
    private DateTime _lastAcceptUtc = DateTime.MinValue;

    public AutoTradeAccept(
        IPluginHost host,
        AutoTradeAcceptSettings settings,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(settings);
        _host = host;
        _settings = settings;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Start()
    {
        if (_onPartnerAccepted is not null)
            return;

        _onPartnerAccepted = OnPartnerAccepted;
        _host.Automation.Trade.PartnerTradeAccepted += _onPartnerAccepted;
    }

    public void Stop()
    {
        if (_onPartnerAccepted is not null)
            _host.Automation.Trade.PartnerTradeAccepted -= _onPartnerAccepted;
        _onPartnerAccepted = null;
    }

    private void OnPartnerAccepted(uint partnerObjectId)
    {
        if (!_settings.Enabled.Value)
            return;

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        if (now - _lastAcceptUtc < RateLimit)
            return;

        if (!_host.Automation.Objects.TryGet(partnerObjectId, out PluginWorldObject partner))
            return;

        foreach (System.Text.RegularExpressions.Regex pattern in _settings.Whitelist)
        {
            if (!pattern.IsMatch(partner.Name))
                continue;

            _host.Automation.Trade.Accept();
            _lastAcceptUtc = now;
            return;
        }
    }
}
