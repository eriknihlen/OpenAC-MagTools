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
/// composed line (with the plain marker/rule-name prefix — see the class
/// remarks on <c>PrintWithVerdict</c> for why this port drops the original's
/// clickable <c>&lt;Tell&gt;</c> wrapper) plus the optional clipboard copy.
/// </summary>
public sealed class ItemInfoPrinter
{
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

        // One snapshot of the owned-items list per Print — it's read both to
        // resolve this object's own inventory record and to build the
        // classifier context's OwnedItems, and the host has no guarantee two
        // separate captures return the same instant's data.
        IReadOnlyList<PluginInventoryItem> ownedItems = _host.Automation.Items.CaptureOwnedItems();
        PluginInventoryItem? inventoryItem = TryResolveInventoryItem(args.ObjectId, ownedItems);
        var item = new ItemModel(worldObject, properties, inventoryItem);
        string infoText = ItemInfoFormatter.Format(item, _settings, _host.Automation.Spells);

        // The original always asked VTank to classify every appraised item,
        // not just owned/open-container ones — a landscape or vendor item
        // being examined before a pickup/purchase decision got a verdict
        // too. Build a synthetic classifier item off the captured properties
        // when there's no real owned/open-container PluginInventoryItem.
        PluginInventoryItem classifierItem = inventoryItem ?? BuildFallbackInventoryItem(worldObject, properties);
        var context = new PluginLootClassificationContext(classifierItem, properties, ownedItems);
        LootVerdict? verdict = _lootRules.Classify(in context);

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

        // The original wrapped the marker in a clickable <Tell:IIDString:...>
        // tag. The host's chat markup parser (AcDream.Core.Chat.ChatTagMarkup,
        // driven by ChatVM.ShouldTagSender) only parses that tag on speech
        // ChatKinds (LocalSpeech/RangedSpeech/Channel/Tell) — a plugin-posted
        // line never gets that treatment, so the tag would render as literal
        // text instead of a clickable name. Emit the plain form instead, same
        // shape as the clipboard line below. See docs/deviations.md.
        string line = marker + ruleTag + " " + infoText;

        _chat.WriteItemInfo(line);

        if (args.AllowAutoClipboard && _settings.AutoClipboard.Value)
            _host.Clipboard.TrySetText(line);
    }

    private PluginInventoryItem? TryResolveInventoryItem(
        uint objectId, IReadOnlyList<PluginInventoryItem> ownedItems)
    {
        foreach (PluginInventoryItem candidate in ownedItems)
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

    /// <summary>
    /// Builds a minimal <see cref="PluginInventoryItem"/> off a world
    /// object's identity and captured properties, for items the classifier
    /// still needs to see even though they aren't in an owned/open-container
    /// list (a landscape corpse-drop or a vendor's wares being examined
    /// before pickup/purchase). Only the fields a classifier plausibly keys
    /// on are filled from the property bag; everything else defaults.
    /// </summary>
    private static PluginInventoryItem BuildFallbackInventoryItem(
        PluginWorldObject worldObject, PluginItemProperties properties)
    {
        int GetInt(int key) => properties.Ints.TryGetValue((uint)key, out int value) ? value : 0;
        double GetFloat(int key) => properties.Floats.TryGetValue((uint)key, out double value) ? value : 0d;

        return new PluginInventoryItem(
            worldObject.ObjectId,
            worldObject.WeenieClassId,
            worldObject.Name,
            worldObject.ItemType,
            worldObject.ContainerObjectId,
            worldObject.WielderObjectId,
            ValidLocations: 0u,
            EquippedLocation: 0u,
            Useability: 0u,
            TargetType: 0u,
            PublicFlags: 0u,
            StackSize: worldObject.StackSize,
            Structure: 0,
            MaximumStructure: 0,
            SpellId: 0u,
            PetClass: 0,
            SummoningMastery: 0,
            ProcSpellId: 0u,
            ProcSpellSelfTargeted: false,
            ProcSpellRate: 0d,
            WeaponSkill: GetInt(ItemModel.WeaponSkillRealKey),
            DamageType: 0,
            Damage: GetInt(ItemModel.DamageRealKey),
            DamageVariance: GetFloat(ItemModel.DamageVarianceRealKey),
            UseRequiresSkill: GetInt(ItemModel.UseRequiresSkillKey),
            UseRequiresSkillLevel: GetInt(ItemModel.UseRequiresSkillLevelKey),
            UseRequiresSkillSpecialized: GetInt(ItemModel.UseRequiresSkillSpecializedKey))
        {
            ObjectClass = worldObject.ObjectClass,
            // WorkmanshipKey (105) is the ordinary whole-number Int
            // workmanship property, not the host's fractional float
            // Workmanship field (salvage-only precision) — don't smuggle one
            // into the other. Leave it 0; nothing in the classifier path
            // needs salvage-precision workmanship for a non-salvage item.
        };
    }
}
