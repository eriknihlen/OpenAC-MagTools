using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Macros;

/// <summary>
/// Port of the original's §1.13 <c>OneTouchHeal</c>: a hotkey-only macro
/// (default unbound) with no setting of its own. Wired through
/// E-HOTKEYS as <c>"One Touch Heal"</c>.
/// </summary>
/// <remarks>
/// Skill id 0x15 ("Healing", <see cref="ItemInfo.Dictionaries.SkillInfo"/>).
/// Kit selection: among owned <see cref="PluginObjectClass.HealingKit"/>
/// items that have appraisal data, the one with the fewest uses remaining
/// (<see cref="PluginInventoryItem.Structure"/> — the port's shape for the
/// original's <c>UsesRemaining</c>); if none are appraised yet, request an id
/// for the first one AND still attempt to apply it in the SAME press (M3) --
/// the original falls through with a 0 bonus rather than waiting a full
/// press cycle for the id to land first. Required skill
/// is <c>2 × missingHealth</c> in Peace, <c>ceil(2.6 × missingHealth)</c>
/// otherwise; effective skill is <c>Skill[Healing].Current +
/// kit.BoostValue</c> (the port's shape for <c>AffectsVitalAmt</c>). Applies
/// the kit when required &lt; effective (≈&gt;50% success) OR there is no
/// food fallback; otherwise falls back to a held Food item whose name
/// contains "Heal" or "Meat". The original's "maybe cast a heal spell"
/// branch was an unimplemented comment there too — not ported.
/// </remarks>
public sealed class OneTouchHeal
{
    public const uint HealingSkillId = 0x15u;

    private readonly IPluginHost _host;

    public OneTouchHeal(IPluginHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <summary>Runs one heal attempt. Returns true when it took an action (applied a kit, requested an id, or used food).</summary>
    public bool TryHeal()
    {
        ICharacterInfo character = _host.Automation.Character;
        int missing = (int)character.MaxHealth - (int)character.CurrentHealth;
        if (missing <= 0)
            return false;

        if (character.TryGetSkill(HealingSkillId, out PluginSkillInfo healing)
            && healing.Training >= PluginSkillTraining.Trained)
        {
            IReadOnlyList<PluginInventoryItem> owned = _host.Automation.Items.CaptureOwnedItems();

            PluginInventoryItem? best = null;
            PluginInventoryItem? unidentified = null;

            foreach (PluginInventoryItem item in owned)
            {
                if (item.ObjectClass != PluginObjectClass.HealingKit)
                    continue;

                bool identified = _host.Automation.Objects.TryGet(item.ObjectId, out PluginWorldObject world)
                    && world.HasAppraisalData;
                if (!identified)
                {
                    unidentified ??= item;
                    continue;
                }

                if (best is null || item.Structure < best.Value.Structure)
                    best = item;
            }

            // M3: when NO kit is appraised yet, the original still falls
            // through and applies the (unappraised) kit in the SAME press --
            // it does not wait a full press cycle for the id to land first.
            // The request goes out AND the apply is attempted, with
            // AffectsVitalAmt/BoostValue simply reading 0 until the id
            // arrives (an un-appraised item carries no appraised bonus).
            if (best is null && unidentified is { } toIdentify)
            {
                _host.Automation.Objects.Identify(toIdentify.ObjectId);
                best = toIdentify;
            }

            if (best is { } kit)
            {
                bool peace = _host.Automation.Combat.Snapshot.Mode == PluginCombatMode.Peace;
                double required = peace ? 2d * missing : Math.Ceiling(2.6d * missing);
                double effective = healing.Current + kit.BoostValue;
                bool haveFood = FindHealingFood(owned) is not null;

                if (required < effective || !haveFood)
                {
                    _host.Automation.Items.Apply(kit.ObjectId, character.ObjectId);
                    return true;
                }
            }
        }

        PluginInventoryItem? food = FindHealingFood(_host.Automation.Items.CaptureOwnedItems());
        if (food is { } chosen)
        {
            _host.Automation.Items.Use(chosen.ObjectId);
            return true;
        }

        return false;
    }

    private static PluginInventoryItem? FindHealingFood(IReadOnlyList<PluginInventoryItem> owned)
    {
        foreach (PluginInventoryItem item in owned)
        {
            if (item.ObjectClass != PluginObjectClass.Food)
                continue;
            if (item.Name.Contains("Heal", StringComparison.Ordinal)
                || item.Name.Contains("Meat", StringComparison.Ordinal))
                return item;
        }

        return null;
    }
}
