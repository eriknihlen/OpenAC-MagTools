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
}
