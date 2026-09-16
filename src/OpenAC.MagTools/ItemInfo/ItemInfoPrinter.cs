using System.Globalization;
using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.ItemInfo;

/// <summary>
/// One item-info line to print, ported from <c>ItemInfoIdentArgs</c>.
/// </summary>
public readonly record struct ItemIdentArgs(
    uint ObjectId,
    bool DontShowIfItemHasNoRule = false,
    bool DontShowIfIsSalvageRule = false,
    bool AllowAutoClipboard = false);

/// <summary>
/// Ports <c>ItemInfoPrinter</c>'s output composition: builds the item info
/// string, asks the active loot classifier for a verdict, and writes the
/// composed line (with or without the clickable <c>&lt;Tell&gt;</c> prefix and
/// loot-rule marker) plus the optional clipboard copy.
/// </summary>
public sealed class ItemInfoPrinter
{
    /// <summary>The original's fixed "tell id" that makes the item-info prefix a clickable link.</summary>
    private const string TellId = "221112";

    private readonly IPluginHost _host;
    private readonly ChatOutput _chat;
    private readonly ItemInfoOnIdentSettings _settings;
    private readonly LootRuleProcessor _lootRules;

    public ItemInfoPrinter(
        IPluginHost host,
        ChatOutput chat,
        ItemInfoOnIdentSettings settings,
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

    public void Print(in ItemIdentArgs args)
    {
        IWorldObjectAutomation objects = _host.Automation.Objects;
        if (!objects.TryGet(args.ObjectId, out PluginWorldObject worldObject))
            return;

        if (!objects.TryCaptureProperties(args.ObjectId, out PluginItemProperties properties))
            properties = ItemModel.EmptyProperties;

        PluginInventoryItem? inventoryItem = TryResolveInventoryItem(args.ObjectId);
        var item = new ItemModel(worldObject, properties, inventoryItem);
        string infoText = ItemInfoFormatter.Format(item, _settings, _host.Automation.Spells);

        LootVerdict? verdict = null;
        if (inventoryItem is { } inv)
        {
            var context = new PluginLootClassificationContext(
                inv, properties, _host.Automation.Items.CaptureOwnedItems());
            verdict = _lootRules.Classify(in context);
        }

        if (verdict is { } v)
        {
            PrintWithVerdict(args, v, infoText);
        }
        else if (!args.DontShowIfItemHasNoRule)
        {
            _chat.WriteItemInfo(infoText);
            if (args.AllowAutoClipboard && _settings.AutoClipboard.Value)
                _host.Clipboard.TrySetText(infoText);
        }
    }

    private void PrintWithVerdict(in ItemIdentArgs args, LootVerdict verdict, string infoText)
    {
        if (args.DontShowIfIsSalvageRule && verdict.IsSalvage)
            return;
        if (args.DontShowIfItemHasNoRule && string.IsNullOrEmpty(verdict.RuleName))
            return;

        string marker = verdict.Passes ? "+" : "-";
        string ruleTag = string.IsNullOrEmpty(verdict.RuleName) ? string.Empty : "(" + verdict.RuleName + ")";

        // The original's literal wrapper is two backslashes before "Tell>",
        // not one — verbatim from ItemInfoPrinter.cs's own `@"<\\Tell>"`.
        string line = "<Tell:IIDString:" + TellId + ":" + args.ObjectId.ToString(CultureInfo.InvariantCulture) + ">"
            + marker + ruleTag + @"<\\Tell> " + infoText;

        _chat.WriteItemInfo(line);

        if (args.AllowAutoClipboard && _settings.AutoClipboard.Value)
        {
            string clipboardLine = marker + ruleTag + " " + infoText;
            _host.Clipboard.TrySetText(clipboardLine);
        }
    }

    private PluginInventoryItem? TryResolveInventoryItem(uint objectId)
    {
        foreach (PluginInventoryItem candidate in _host.Automation.Items.CaptureOwnedItems())
        {
            if (candidate.ObjectId == objectId)
                return candidate;
        }

        foreach (PluginInventoryItem candidate in _host.Automation.Loot.CaptureCurrentContents())
        {
            if (candidate.ObjectId == objectId)
                return candidate;
        }

        return null;
    }
}
