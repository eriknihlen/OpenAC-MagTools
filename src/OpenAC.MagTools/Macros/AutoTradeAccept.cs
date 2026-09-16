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
