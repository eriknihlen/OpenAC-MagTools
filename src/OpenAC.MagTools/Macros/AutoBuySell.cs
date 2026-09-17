using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Macros;

/// <summary>
/// Port of the original's §1.5 <c>AutoBuySell</c>: drive a vendor's shop pane
/// off a <c>&lt;VendorName&gt;.utl</c> loot profile — buy everything the
/// profile keeps, sell everything it marks Sell, one item of each per round,
/// until nothing is left to do. Settings: <c>AutoBuySell/Enabled</c> (default
/// true), <c>AutoBuySell/TestMode</c> (default false, force-enables Enabled).
/// </summary>
/// <remarks>
/// <para>
/// The original waited on <c>WorldFilter.ApproachVendor</c>, loaded VTClassic
/// lazily, and drove Virindi Item Tool's own combined
/// <c>BeginSellBuy(sell, buy, amount)</c> call, polling
/// <c>ActivityStateChanged</c> back to Idle before picking the next pair. This
/// port's trigger is <see cref="IVendorAutomation.Opened"/> (the shop pane
/// becoming the open one is this host's equivalent of "approached and ready"
/// — there is no separate pre-open approach event), the profile is the
/// classifier's E-LOOT <c>TryClassifyWithProfile</c> against the vendor's own
/// world-object name, and the host's <see cref="IVendorAutomation"/> only
/// exposes separate <c>BuyAll</c>/<c>SellAll</c> commits rather than one
/// combined call, so a round here is buy-then-sell (driven off
/// <see cref="IVendorAutomation.TransactionCompleted"/>) instead of one
/// atomic operation. See docs/deviations.md.
/// </para>
/// <para>
/// The original's <c>KickOffBuySell</c> also carried a known upstream NRE
/// (Appendix B #1: dereferencing the possibly-null buy/sell item before the
/// null check) — this port reproduces the INTENT (skip and report when
/// either side has nothing to do), not the crash.
/// </para>
/// </remarks>
public sealed class AutoBuySell
{
    /// <summary>
    /// The original's own comment: "can't add more than 5000 of any one item
    /// to the vendor buy pane."
    /// </summary>
    private const int MaxBuyCount = 5000;

    private static readonly TimeSpan ThinkInterval = TimeSpan.FromMilliseconds(100);

    private enum Phase
    {
        Idle,
        Buying,
        Selling,
    }

    private readonly IPluginHost _host;
    private readonly ChatOutput _chat;
    private readonly AutoBuySellSettings _settings;
    private readonly LootRuleProcessor _lootRules;

    private IDisposable? _thinkRegistration;
    private Action<uint>? _onOpened;
    private Action? _onClosed;
    private Action<PluginVendorTransaction>? _onTransactionCompleted;
    private string _vendorProfileName = string.Empty;
    private Phase _phase;
    private bool _active;

    public AutoBuySell(
        IPluginHost host,
        ChatOutput chat,
        AutoBuySellSettings settings,
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

        _onOpened = OnVendorOpened;
        _onClosed = OnVendorClosed;
        _onTransactionCompleted = OnTransactionCompleted;
        _host.Automation.Vendor.Opened += _onOpened;
        _host.Automation.Vendor.Closed += _onClosed;
        _host.Automation.Vendor.TransactionCompleted += _onTransactionCompleted;

        _thinkRegistration = scheduler.Every(ThinkInterval, Think);
    }

    public void Stop()
    {
        _thinkRegistration?.Dispose();
        _thinkRegistration = null;

        if (_onOpened is not null)
            _host.Automation.Vendor.Opened -= _onOpened;
        _onOpened = null;
        if (_onClosed is not null)
            _host.Automation.Vendor.Closed -= _onClosed;
        _onClosed = null;
        if (_onTransactionCompleted is not null)
            _host.Automation.Vendor.TransactionCompleted -= _onTransactionCompleted;
        _onTransactionCompleted = null;

        _active = false;
        _phase = Phase.Idle;
    }

    private void OnVendorOpened(uint vendorObjectId)
    {
        if (!_settings.Enabled.Value)
            return;

        // LOW: read straight off the vendor surface's own VendorName rather
        // than re-resolving the vendor through the general world-object
        // table -- IVendorAutomation already carries it, and a vendor is not
        // guaranteed to also be present as an ordinary tracked world object.
        _vendorProfileName = _host.Automation.Vendor.VendorName;
        _phase = Phase.Idle;

        if (_settings.TestMode.Value)
        {
            // TestMode never drives the wire — it only reports what would
            // happen, exactly like the original's DoTestMode.
            _active = false;
            DoTestMode();
            return;
        }

        _active = true;
    }

    private void OnVendorClosed()
    {
        _active = false;
        _phase = Phase.Idle;
    }

    private void OnTransactionCompleted(PluginVendorTransaction result)
    {
        if (!_active)
            return;

        // Defect 14a: this previously ignored Success/Notice entirely and
        // proceeded identically whether the server actually completed the
        // trade or rejected it (insufficient funds, a stale price, an
        // over-burden/no-pack-space refusal) -- a failed buy or sell was
        // indistinguishable from a real one.
        if (!result.Success)
        {
            _chat.Write("AutoBuySell: "
                + (result.Kind == PluginVendorTransactionKind.Buy ? "Buy" : "Sell")
                + " failed"
                + (string.IsNullOrWhiteSpace(result.Notice) ? "." : ": " + result.Notice));
        }

        if (result.Kind == PluginVendorTransactionKind.Buy && _phase == Phase.Buying)
        {
            SellPick? sell = GetSellItem();
            if (sell is { } picked)
            {
                if (!TryVendorCommand(_host.Automation.Vendor.AddToSellList(picked.ObjectId), "AddToSellList"))
                    return;
                if (!TryVendorCommand(_host.Automation.Vendor.SellAll(), "SellAll"))
                    return;
                _phase = Phase.Selling;
            }
            else
            {
                _chat.Write("AutoBuySell: Nothing to Sell");
                _phase = Phase.Idle;
            }
        }
        else if (result.Kind == PluginVendorTransactionKind.Sell && _phase == Phase.Selling)
        {
            _phase = Phase.Idle;
        }
    }

    /// <summary>
    /// Defect 14a (live-gate round 4): <c>AddToBuyList</c>/<c>BuyAll</c>/
    /// <c>AddToSellList</c>/<c>SellAll</c> results were discarded outright.
    /// A refusal (Busy, InvalidItem, NotOpen, Unavailable) meant no wire
    /// command was ever sent, yet the caller still advanced
    /// <see cref="_phase"/> to Buying/Selling and then waited forever for a
    /// <see cref="IVendorAutomation.TransactionCompleted"/> that would never
    /// arrive -- wedging this vendor visit's automation silently for the
    /// rest of the session. Reports the refusal and resets to
    /// <see cref="Phase.Idle"/>/<c>_active = false</c> instead of pretending
    /// the round is still in flight.
    /// </summary>
    private bool TryVendorCommand(PluginVendorCommandResult result, string action)
    {
        if (result.Status == PluginVendorCommandStatus.Sent)
            return true;

        _chat.Write("AutoBuySell: " + action + " refused: "
            + (string.IsNullOrWhiteSpace(result.Notice) ? result.Status.ToString() : result.Notice));
        _active = false;
        _phase = Phase.Idle;
        return false;
    }

    private void Think()
    {
        if (!_active || _phase != Phase.Idle)
            return;
        if (!_host.Automation.Vendor.IsOpen)
        {
            _active = false;
            return;
        }
        if (_host.Automation.Vendor.IsBusy)
            return;

        BuyPick? buy = GetBuyItem();
        SellPick? sell = GetSellItem();

        // M2: reported independently -- either side (or both) can be empty
        // on a given round, and the original's own comment insists both get
        // their own line. LOW: this now runs BEFORE the "both empty, stop"
        // check below, so a round that ends the vendor visit still reports
        // both "Nothing to Buy" AND "Nothing to Sell" once, instead of
        // silently stopping without either message.
        if (buy is null)
            _chat.Write("AutoBuySell: Nothing to Buy");
        if (sell is null)
            _chat.Write("AutoBuySell: Nothing to Sell");

        if (buy is null && sell is null)
        {
            // Nothing left on either side of the profile — the round-trip
            // loop is done for this vendor visit.
            _active = false;
            return;
        }

        // M1 (corrected in the third fix round): when BOTH picks exist,
        // upstream checks whether either is a TradeNote. Trading a
        // TradeNote in for another TradeNote is never useful, so BOTH being
        // TradeNotes refuses the round silently (no wire commands, no
        // message -- reproducing the INTENT of the guard the original's
        // NRE, Appendix B #1, was meant to provide). NEITHER being a
        // TradeNote instead prints "No TradeNotes to buy or sell. Check Loot
        // Profile" as an informational note and still proceeds -- it is not
        // a refusal. A MIXED pick (exactly one is a TradeNote) gets neither
        // message and proceeds normally.
        if (buy is { } buyPick && sell is { } sellPick)
        {
            bool buyIsNote = buyPick.ObjectClass == PluginObjectClass.TradeNote;
            bool sellIsNote = sellPick.ObjectClass == PluginObjectClass.TradeNote;

            if (buyIsNote && sellIsNote)
                return;

            if (!buyIsNote && !sellIsNote)
                _chat.Write("AutoBuySell: No TradeNotes to buy or sell. Check Loot Profile");
        }

        if (buy is { } picked)
        {
            if (!TryVendorCommand(_host.Automation.Vendor.AddToBuyList(picked.TemplateObjectId, picked.Count), "AddToBuyList"))
                return;
            if (!TryVendorCommand(_host.Automation.Vendor.BuyAll(), "BuyAll"))
                return;
            _phase = Phase.Buying;
            return;
        }

        if (sell is { } sellPicked)
        {
            if (!TryVendorCommand(_host.Automation.Vendor.AddToSellList(sellPicked.ObjectId), "AddToSellList"))
                return;
            if (!TryVendorCommand(_host.Automation.Vendor.SellAll(), "SellAll"))
                return;
            _phase = Phase.Selling;
        }
    }

    private readonly record struct BuyPick(uint TemplateObjectId, int Count, PluginObjectClass ObjectClass);

    private readonly record struct SellPick(uint ObjectId, PluginObjectClass ObjectClass);

    /// <summary>
    /// First pass: a Keep-Up-To rule whose owned count (by matching name,
    /// stack-aware) is still under its <c>Data1</c>/KeepCount — buy the
    /// shortfall, clamped to <see cref="MaxBuyCount"/>. Second pass: any
    /// plain Keep rule, bought at the max clamp.
    /// </summary>
    private BuyPick? GetBuyItem()
    {
        IReadOnlyList<PluginVendorItem> items = _host.Automation.Vendor.Items;
        IReadOnlyList<PluginInventoryItem> owned = _host.Automation.Items.CaptureOwnedItems();

        foreach (PluginVendorItem item in items)
        {
            if (!TryClassifyVendorItem(item, out PluginLootClassification classification))
                continue;
            if (classification.Action != PluginLootAction.KeepUpTo)
                continue;

            int have = CountOwnedMatching(owned, item.Name);
            if (have >= classification.KeepCount)
                continue;

            int amount = Math.Min(classification.KeepCount - have, MaxBuyCount);
            if (amount <= 0)
                continue;

            return new BuyPick(item.TemplateObjectId, amount, item.ObjectClass);
        }

        foreach (PluginVendorItem item in items)
        {
            if (!TryClassifyVendorItem(item, out PluginLootClassification classification))
                continue;
            if (classification.Action != PluginLootAction.Keep)
                continue;

            return new BuyPick(item.TemplateObjectId, MaxBuyCount, item.ObjectClass);
        }

        return null;
    }

    /// <summary>
    /// Priority: the first non-SpellComponent/non-TradeNote owned item the
    /// profile marks Sell; else the cheapest (by <c>Value</c>) non-TradeNote
    /// match (this pass DOES include SpellComponent); else the cheapest
    /// TradeNote match.
    /// </summary>
    private SellPick? GetSellItem()
    {
        PluginInventoryItem? firstPlain = null;
        PluginInventoryItem? cheapestNonNote = null;
        PluginInventoryItem? cheapestNote = null;

        foreach (PluginInventoryItem item in _host.Automation.Items.CaptureOwnedItems())
        {
            if (item.IsEquipped)
                continue;
            if (!TryClassifySellItem(item, out PluginLootClassification classification))
                continue;
            if (classification.Action != PluginLootAction.Sell)
                continue;

            bool isNote = item.ObjectClass == PluginObjectClass.TradeNote;
            bool isComponent = item.ObjectClass == PluginObjectClass.SpellComponent;

            if (!isNote)
            {
                if (!isComponent && firstPlain is null)
                    firstPlain = item;
                if (cheapestNonNote is null || item.Value < cheapestNonNote.Value.Value)
                    cheapestNonNote = item;
            }
            else if (cheapestNote is null || item.Value < cheapestNote.Value.Value)
            {
                cheapestNote = item;
            }
        }

        PluginInventoryItem? chosen = firstPlain ?? cheapestNonNote ?? cheapestNote;
        return chosen is { } item2 ? new SellPick(item2.ObjectId, item2.ObjectClass) : null;
    }

    private static int CountOwnedMatching(IReadOnlyList<PluginInventoryItem> owned, string name)
    {
        int total = 0;
        foreach (PluginInventoryItem item in owned)
        {
            if (!string.Equals(item.Name, name, StringComparison.Ordinal))
                continue;
            total += item.StackSize > 0 ? item.StackSize : 1;
        }

        return total;
    }

    private bool TryClassifyVendorItem(
        PluginVendorItem item,
        out PluginLootClassification classification)
    {
        _host.Automation.Vendor.TryCaptureProperties(item.TemplateObjectId, out PluginItemProperties properties);
        PluginInventoryItem classifyItem = LootRuleProcessor.BuildMinimalItem(
            item.TemplateObjectId, item.WeenieClassId, item.Name, item.ObjectClass,
            item.StackSize, item.UnitPrice);
        var context = new PluginLootClassificationContext(
            classifyItem, properties, Array.Empty<PluginInventoryItem>());
        return _lootRules.TryClassifyWithProfile(_vendorProfileName, in context, out classification);
    }

    private bool TryClassifySellItem(
        PluginInventoryItem item,
        out PluginLootClassification classification)
    {
        _host.Automation.Items.TryCaptureProperties(item.ObjectId, out PluginItemProperties properties);
        var context = new PluginLootClassificationContext(
            item, properties, Array.Empty<PluginInventoryItem>());
        return _lootRules.TryClassifyWithProfile(_vendorProfileName, in context, out classification);
    }

    private void DoTestMode()
    {
        _chat.Write("Buy Items:");
        foreach (PluginVendorItem item in _host.Automation.Vendor.Items)
        {
            if (TryClassifyVendorItem(item, out PluginLootClassification classification)
                && classification.Action is PluginLootAction.Keep or PluginLootAction.KeepUpTo)
                _chat.Write(item.Name);
        }

        _chat.Write("Sell Items:");
        foreach (PluginInventoryItem item in _host.Automation.Items.CaptureOwnedItems())
        {
            if (item.IsEquipped)
                continue;
            if (TryClassifySellItem(item, out PluginLootClassification classification)
                && classification.Action == PluginLootAction.Sell)
                _chat.Write(item.Name);
        }
    }
}
