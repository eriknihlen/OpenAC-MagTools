using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.ItemInfo;

/// <summary>One classifier's verdict on an item, in the shape the item-info printer needs.</summary>
public readonly record struct LootVerdict(bool Passes, bool IsSalvage, string RuleName);

/// <summary>
/// Ports <c>VirindiTankLootRuleProcessor</c>: pick the registered loot
/// classifier (the port's MossTank-equivalent stand-in for VTank), and
/// translate its verdict into the shape <see cref="ItemInfoPrinter"/> needs.
/// </summary>
/// <remarks>
/// The original juggled a live-profile async callback path
/// (<c>FLootPluginQueryNeedsID</c> + <c>FLootPluginClassifyCallback</c>) versus
/// a synchronous one (<c>FLootPluginClassifyImmediate</c>), because VTank's ID
/// queue could take more than one frame. The host contract's
/// <see cref="IPluginLootClassifierRegistry.TryClassify"/> is already
/// synchronous — there is no separate async id-request path to bridge — so
/// this port collapses to one synchronous call. A "no classifier" registry
/// (nothing registered — the port's equivalent of "VTank isn't loaded")
/// returns null, exactly like the original falling back to plain,
/// rule-less printing.
/// </remarks>
public sealed class LootRuleProcessor
{
    private static readonly string[] ManaRuleActionNames = ["ManaStone", "ManaTank"];

    private readonly IPluginLootClassifierRegistry _registry;

    public LootRuleProcessor(IPluginLootClassifierRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>
    /// The classifier this processor talks to: the first registered one whose
    /// id contains "moss-tank" (case-insensitive), or else the first
    /// registered classifier at all. Re-resolved on every call, since a
    /// classifier can register or unregister across the plugin's lifetime.
    /// </summary>
    public string? ActiveClassifierId
    {
        get
        {
            IReadOnlyList<PluginLootClassifierInfo> available = _registry.Available;
            if (available.Count == 0)
                return null;

            foreach (PluginLootClassifierInfo candidate in available)
            {
                if (candidate.Id.Contains("moss-tank", StringComparison.OrdinalIgnoreCase))
                    return candidate.Id;
            }

            return available[0].Id;
        }
    }

    /// <summary>
    /// Classifies the item against the live profile. Returns null when no
    /// classifier is registered — the caller falls back to the plain print.
    /// </summary>
    public LootVerdict? Classify(in PluginLootClassificationContext context)
    {
        string? classifierId = ActiveClassifierId;
        if (classifierId is null)
            return null;

        if (!_registry.TryClassify(classifierId, in context, out PluginLootClassification classification))
            return null;

        // The original's own check, verbatim: !result.IsNoLoot. An authored
        // .utl rule that legitimately resolves to NoLoot (named or not)
        // fails and prints "-(Rule)".
        bool passes = classification.Matched && classification.Action != PluginLootAction.NoLoot;

        // ManaStone/ManaTank currently come back Matched:true, Action:NoLoot
        // from the classifier registry — a HOST artefact of the CURRENT
        // OpenAC snapshot, where the shared PluginLootAction enum has no
        // ManaStone/ManaTank members yet, not a real NoLoot verdict from the
        // original's semantics. OpenAC is adding those members; matched by
        // NAME via Enum.TryParse (never a direct PluginLootAction.ManaStone/
        // .ManaTank reference) so this keeps compiling against both the
        // current snapshot (TryParse simply fails, this is a no-op) and the
        // fixed one (Action already differs from NoLoot by then, so this is
        // a harmless belt-and-braces match, not the primary path). See
        // docs/deviations.md.
        if (!passes && classification.Matched)
            passes = IsManaRuleAction(classification.Action);

        bool isSalvage = classification.Action == PluginLootAction.Salvage;
        return new LootVerdict(passes, isSalvage, classification.RuleName);
    }

    private static bool IsManaRuleAction(PluginLootAction action)
    {
        foreach (string name in ManaRuleActionNames)
        {
            if (Enum.TryParse(name, ignoreCase: true, out PluginLootAction candidate) && action == candidate)
                return true;
        }

        return false;
    }

    /// <summary>Forwards to the active classifier's <c>NeedsIdentification</c>.</summary>
    public bool NeedsIdentification(in PluginLootClassificationContext context)
    {
        string? classifierId = ActiveClassifierId;
        return classifierId is not null && _registry.TryNeedsIdentification(classifierId, in context);
    }

    /// <summary>
    /// The raw live-profile verdict (not the printer's folded
    /// <see cref="LootVerdict"/>) for callers that need
    /// <see cref="PluginLootClassification.Action"/> and
    /// <see cref="PluginLootClassification.KeepCount"/> directly — the
    /// looter and inventory packer both branch on Keep vs. KeepUpTo vs.
    /// Sell, which <see cref="Classify"/>'s folded Passes/IsSalvage shape
    /// does not expose. Returns false (and a default classification) when
    /// there is no active classifier or the item did not match any rule.
    /// </summary>
    public bool TryClassifyLive(
        in PluginLootClassificationContext context,
        out PluginLootClassification classification)
    {
        string? classifierId = ActiveClassifierId;
        if (classifierId is null)
        {
            classification = default;
            return false;
        }

        return _registry.TryClassify(classifierId, in context, out classification)
            && classification.Matched;
    }

    /// <summary>
    /// Classifies against a named, stored profile (a vendor's, a trade
    /// partner's, or an auto-pack profile) rather than the classifier's live
    /// one — E-LOOT's <see cref="IPluginLootClassifierRegistry.TryClassifyWithProfile"/>.
    /// Returns false when there is no active classifier, the named profile
    /// does not exist, or the item did not match any rule in it.
    /// </summary>
    public bool TryClassifyWithProfile(
        string profileName,
        in PluginLootClassificationContext context,
        out PluginLootClassification classification)
    {
        string? classifierId = ActiveClassifierId;
        if (classifierId is null)
        {
            classification = default;
            return false;
        }

        return _registry.TryClassifyWithProfile(classifierId, profileName, in context, out classification)
            && classification.Matched;
    }

    /// <summary>
    /// True when <paramref name="profileName"/> resolves to a real, loaded
    /// profile for the active classifier: a probe classification call
    /// against an empty item returns <c>true</c> as long as the named
    /// profile itself exists, per E-LOOT's contract ("returns false when the
    /// named profile does not exist") — independent of whether the probe
    /// item happens to match any rule in it.
    /// </summary>
    public bool ProfileExists(string profileName)
    {
        string? classifierId = ActiveClassifierId;
        if (classifierId is null)
            return false;

        var probe = new PluginLootClassificationContext(
            BuildMinimalItem(0u, 0u, string.Empty, PluginObjectClass.Unknown),
            default,
            Array.Empty<PluginInventoryItem>());
        return _registry.TryClassifyWithProfile(
            classifierId, profileName, in probe, out _);
    }

    /// <summary>
    /// Builds a minimal <see cref="PluginInventoryItem"/> good enough for a
    /// loot-rule classification call when the real automation surface only
    /// hands back a shape other than <see cref="PluginInventoryItem"/> (a
    /// vendor listing, a probe item) — every field the classifier does not
    /// need for name/class/value-based rules stays at its default.
    /// </summary>
    public static PluginInventoryItem BuildMinimalItem(
        uint objectId,
        uint weenieClassId,
        string name,
        PluginObjectClass objectClass,
        int stackSize = 1,
        int value = 0)
        => new(
            objectId, weenieClassId, name, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u,
            stackSize, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ObjectClass = objectClass,
            Value = value,
        };
}
