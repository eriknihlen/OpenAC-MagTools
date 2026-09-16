using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Macros;

/// <summary>
/// Port of the original's §1.6 <c>AutoTradeAdd</c>: while a trade window is
/// open, request ids for whatever still needs one, then add owned items the
/// trade partner's own <c>&lt;PartnerName&gt;.utl</c> profile marks Keep, one
/// per 100 ms think. Setting: <c>AutoTradeAdd/Enabled</c> (default false).
/// </summary>
/// <remarks>
/// The original resolved "the other party" from
/// <c>WorldFilter.EnterTrade(TraderId, TradeeId)</c> with a documented quirk
/// (the receiving side sees itself as both ids, to stop a mule from
/// auto-muling back). <see cref="ITradeAutomation"/> already normalizes this:
/// <c>Opened.PartnerObjectId</c> is always the other side regardless of who
/// asked, and <c>PartnerName</c> is already resolved — the quirk has nothing
/// left to work around here. See docs/deviations.md.
/// <para>
/// The host has no per-profile "needs id" query (E-LOOT's
/// <c>NeedsIdentification</c> is scoped to the classifier's LIVE profile, not
/// a named one) — this port approximates the original's
/// <c>DoesPotentialItemNeedID</c> gate with "lacks appraisal data at all",
/// requesting an id once per item and waiting for
/// <see cref="PluginWorldObject.HasAppraisalData"/> before classifying it.
/// See docs/deviations.md.
/// </para>
/// </remarks>
public sealed class AutoTradeAdd
{
    private static readonly TimeSpan ThinkInterval = TimeSpan.FromMilliseconds(100);

    private readonly IPluginHost _host;
    private readonly ChatOutput _chat;
    private readonly AutoTradeAddSettings _settings;
    private readonly LootRuleProcessor _lootRules;

    private IDisposable? _thinkRegistration;
    private Action<PluginTradeOpened>? _onOpened;
    private Action? _onClosed;
    private string _profileName = string.Empty;
    private readonly HashSet<uint> _idRequested = [];
    private readonly HashSet<uint> _added = [];
    private bool _active;
    private bool _completeReported;

    public AutoTradeAdd(
        IPluginHost host,
        ChatOutput chat,
        AutoTradeAddSettings settings,
        LootRuleProcessor lootRules)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(lootRules);
        _host = host;
        _chat = chat;
        _settings = settings;
        _lootRules = lootRules;
    }

    public void Start(TickScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (_thinkRegistration is not null)
            return;

        _onOpened = OnTradeOpened;
        _onClosed = OnTradeClosed;
        _host.Automation.Trade.Opened += _onOpened;
        _host.Automation.Trade.Closed += _onClosed;

        _thinkRegistration = scheduler.Every(ThinkInterval, Think);
    }

    public void Stop()
    {
        _thinkRegistration?.Dispose();
        _thinkRegistration = null;

        if (_onOpened is not null)
            _host.Automation.Trade.Opened -= _onOpened;
        _onOpened = null;
        if (_onClosed is not null)
            _host.Automation.Trade.Closed -= _onClosed;
        _onClosed = null;

        ResetRun();
    }

    private void OnTradeOpened(PluginTradeOpened opened)
    {
        if (!_settings.Enabled.Value)
            return;

        _profileName = _host.Automation.Trade.PartnerName;
        _idRequested.Clear();
        _added.Clear();
        _completeReported = false;
        _active = true;
    }

    private void OnTradeClosed() => ResetRun();

    private void ResetRun()
    {
        _active = false;
        _idRequested.Clear();
        _added.Clear();
        _completeReported = false;
    }

    private void Think()
    {
        if (!_active)
            return;
        if (!_host.Automation.Trade.IsOpen)
        {
            ResetRun();
            return;
        }

        bool anyPendingId = false;

        foreach (PluginInventoryItem item in _host.Automation.Items.CaptureOwnedItems())
        {
            if (item.IsEquipped || _added.Contains(item.ObjectId))
                continue;

            if (!TryHasAppraisal(item.ObjectId))
            {
                if (_idRequested.Add(item.ObjectId))
                    _host.Automation.Objects.Identify(item.ObjectId);
                anyPendingId = true;
            }
        }

        // One add per think, after id requests have gone out this tick.
        foreach (PluginInventoryItem item in _host.Automation.Items.CaptureOwnedItems())
        {
            if (item.IsEquipped || _added.Contains(item.ObjectId))
                continue;
            if (!TryHasAppraisal(item.ObjectId))
                continue;

            if (!TryClassify(item, out PluginLootClassification classification))
                continue;
            if (classification.Action != PluginLootAction.Keep)
                continue;

            _host.Automation.Trade.Add(item.ObjectId);
            _added.Add(item.ObjectId);
            return;
        }

        if (!anyPendingId && !_completeReported)
        {
            _completeReported = true;
            _chat.Write("Auto Add To Trade - Inventory scan complete.");
            _active = false;
        }
    }

    private bool TryHasAppraisal(uint objectId)
        => _host.Automation.Objects.TryGet(objectId, out PluginWorldObject world)
            && world.HasAppraisalData;

    private bool TryClassify(PluginInventoryItem item, out PluginLootClassification classification)
    {
        _host.Automation.Items.TryCaptureProperties(item.ObjectId, out PluginItemProperties properties);
        var context = new PluginLootClassificationContext(
            item, properties, Array.Empty<PluginInventoryItem>());
        return _lootRules.TryClassifyWithProfile(_profileName, in context, out classification);
    }
}
