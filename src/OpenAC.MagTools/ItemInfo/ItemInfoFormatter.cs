using System.Globalization;
using System.Text;
using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.ItemInfo;

/// <summary>
/// Ports the original <c>ItemInfo.ToString()</c> — the 28-segment,
/// comma-joined item info line — verbatim, segment order and all. See
/// the port design's §1.3 (numbered exactly the same way in the comments
/// below) for the segment table this mirrors.
/// </summary>
public static class ItemInfoFormatter
{
    public static string Format(ItemModel item, ISettings settings, ISpellCatalog spells)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(spells);

        var sb = new StringBuilder();

        // 1. Material prefix.
        if (item.MaterialName is { } material)
            sb.Append(material).Append(' ');

        // 2. Name.
        sb.Append(item.Name);

        // 3. Mastery.
        if (item.MasteryName is { } mastery)
            sb.Append(" (").Append(mastery).Append(')');

        // 4. Attribute set.
        if (item.AttributeSetName is { } set)
            sb.Append(", ").Append(set);

        // 5. Armor level.
        int armorLevel = item.GetInt(ItemModel.ArmorLevelKey, 0);
        if (armorLevel > 0)
            sb.Append(", AL ").Append(armorLevel.ToString(CultureInfo.InvariantCulture));

        // 6. Imbue flags.
        int imbued = item.GetInt(ItemModel.ImbuedKey, 0);
        if (imbued > 0)
        {
            sb.Append(',');
            AppendImbueFlag(sb, imbued, 1, " CS");
            AppendImbueFlag(sb, imbued, 2, " CB");
            AppendImbueFlag(sb, imbued, 4, " AR");
            AppendImbueFlag(sb, imbued, 8, " SlashRend");
            AppendImbueFlag(sb, imbued, 16, " PierceRend");
            AppendImbueFlag(sb, imbued, 32, " BludgeRend");
            AppendImbueFlag(sb, imbued, 64, " AcidRend");
            AppendImbueFlag(sb, imbued, 128, " FrostRend");
            AppendImbueFlag(sb, imbued, 256, " LightRend");
            AppendImbueFlag(sb, imbued, 512, " FireRend");
            AppendImbueFlag(sb, imbued, 1024, " MeleeImbue");
            AppendImbueFlag(sb, imbued, 4096, " MagicImbue");
            AppendImbueFlag(sb, imbued, 8192, " Hematited");
            AppendImbueFlag(sb, imbued, 536870912, " MagicAbsorb");
        }

        // 7. Tinks.
        int tinks = item.GetInt(ItemModel.NumberTimesTinkeredKey, 0);
        if (tinks > 0)
            sb.Append(", Tinks ").Append(tinks.ToString(CultureInfo.InvariantCulture));

        // 8. Damage.
        int maxDamage = item.MaxDamage;
        double variance = item.Variance;
        if (maxDamage != 0 && variance != 0)
        {
            double minDamage = maxDamage - maxDamage * variance;
            sb.Append(", ").Append(minDamage.ToString("N2", CultureInfo.InvariantCulture))
                .Append('-').Append(maxDamage.ToString(CultureInfo.InvariantCulture));
        }
        else if (maxDamage != 0 && variance == 0)
        {
            sb.Append(", ").Append(maxDamage.ToString(CultureInfo.InvariantCulture));
        }

        // 9. Elemental damage bonus.
        int elementalDmgBonus = item.GetInt(ItemModel.ElementalDmgBonusKey, 0);
        if (elementalDmgBonus != 0)
            sb.Append(", +").Append(elementalDmgBonus.ToString(CultureInfo.InvariantCulture));

        // 10. Damage bonus %.
        double damageBonus = item.DamageBonus;
        if (damageBonus != 1)
            sb.Append(", +").Append(RoundToString(damageBonus)).Append('%');

        // 11. Elemental damage vs. monsters %.
        double vsMonsters = item.GetDouble(ItemModel.ElementalDamageVersusMonstersKey, 1);
        if (vsMonsters != 1)
            sb.Append(", +").Append(RoundToString(vsMonsters)).Append("%vs. Monsters");

        // 12. Attack bonus %.
        double attackBonus = item.AttackBonus;
        if (attackBonus != 1)
            sb.Append(", +").Append(RoundToString(attackBonus)).Append("%a");

        // 13. Melee defense bonus %.
        double meleeDefense = item.GetDouble(ItemModel.MeleeDefenseBonusKey, 1);
        if (meleeDefense != 1)
            sb.Append(", ").Append(RoundToString(meleeDefense)).Append("%md");

        // 14. Magic defense bonus %.
        double magicDefense = item.GetDouble(ItemModel.MagicDBonusKey, 1);
        if (magicDefense != 1)
            sb.Append(", ")
                .Append(Math.Round((magicDefense - 1) * 100, 1).ToString(CultureInfo.InvariantCulture))
                .Append("%mgc.d");

        // 15. Missile defense bonus %.
        double missileDefense = item.GetDouble(ItemModel.MissileDBonusKey, 1);
        if (missileDefense != 1)
            sb.Append(", ")
                .Append(Math.Round((missileDefense - 1) * 100, 1).ToString(CultureInfo.InvariantCulture))
                .Append("%msl.d");

        // 16. Mana conversion bonus %.
        double manaCBonus = item.GetDouble(ItemModel.ManaCBonusKey, 0);
        if (manaCBonus != 0)
            sb.Append(", ")
                .Append(Math.Round(manaCBonus * 100).ToString(CultureInfo.InvariantCulture))
                .Append("%mc");

        // 17. Buffed block.
        if (settings.ShowBuffedValues
            && (item.ObjectClass == PluginObjectClass.MeleeWeapon
                || item.ObjectClass == PluginObjectClass.MissileWeapon
                || item.ObjectClass == PluginObjectClass.WandStaffOrb))
        {
            AppendBuffedBlock(sb, item);
        }

        // 18. Spells.
        AppendSpells(sb, item, spells);

        // 19. Wield requirement.
        int wieldReqValue = item.GetInt(ItemModel.WieldReqValueKey, 0);
        if (wieldReqValue > 0)
        {
            int wieldReqType = item.GetInt(ItemModel.WieldReqTypeKey, 0);
            int wieldReqAttribute = item.GetInt(ItemModel.WieldReqAttributeKey, 0);
            if (wieldReqType == 7 && wieldReqAttribute == 1)
            {
                sb.Append(", Wield Lvl ").Append(wieldReqValue.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(", ").Append(ItemModel.SkillName(wieldReqAttribute))
                    .Append(' ').Append(wieldReqValue.ToString(CultureInfo.InvariantCulture));
            }
        }

        // 20. Summoning gem level.
        int summoningLevel = item.GetInt(ItemModel.SummoningGemLevelKey, 0);
        if (summoningLevel > 0)
            sb.Append(", Lvl ").Append(summoningLevel.ToString(CultureInfo.InvariantCulture));

        // 21. Activation requirement.
        int skillLevelReq = item.GetInt(ItemModel.SkillLevelReqKey, 0);
        if (skillLevelReq > 0)
        {
            int wieldReqAttribute = item.GetInt(ItemModel.WieldReqAttributeKey, 0);
            int activationSkillId = item.GetInt(ItemModel.ActivationReqSkillIdKey, 0);
            int activationWieldReqValue = item.GetInt(ItemModel.WieldReqValueKey, 0);
            if (wieldReqAttribute != activationSkillId || activationWieldReqValue < skillLevelReq)
            {
                sb.Append(", ").Append(ItemModel.SkillName(activationSkillId))
                    .Append(' ').Append(skillLevelReq.ToString(CultureInfo.InvariantCulture))
                    .Append(" to Activate");
            }
        }

        // 22. Summoning gem use-requires-skill / spec.
        int useSkill = item.GetInt(ItemModel.UseRequiresSkillKey, 0);
        int useSkillLevel = item.GetInt(ItemModel.UseRequiresSkillLevelKey, 0);
        if (useSkill > 0 && useSkillLevel > 0)
            sb.Append(", ").Append(ItemModel.SkillName(useSkill))
                .Append(' ').Append(useSkillLevel.ToString(CultureInfo.InvariantCulture));

        int useSkillSpec = item.GetInt(ItemModel.UseRequiresSkillSpecializedKey, 0);
        if (useSkillSpec > 0 && useSkillLevel > 0)
        {
            // The original's unknown-skill message for THIS segment reads
            // "Unknown skill spec: N M", not "Spec Unknown skill: N M" — a
            // different literal from the plain "Unknown skill: N M" every
            // other segment uses, so it can't share ItemModel.SkillName's
            // generic fallback text.
            if (Dictionaries.SkillInfo.TryGetValue(useSkillSpec, out string? specName))
                sb.Append(", Spec ").Append(specName)
                    .Append(' ').Append(useSkillLevel.ToString(CultureInfo.InvariantCulture));
            else
                sb.Append(", Unknown skill spec: ").Append(useSkillSpec.ToString(CultureInfo.InvariantCulture))
                    .Append(' ').Append(useSkillLevel.ToString(CultureInfo.InvariantCulture));
        }

        // 23. Lore requirement.
        int loreRequirement = item.GetInt(ItemModel.LoreRequirementKey, 0);
        if (loreRequirement > 0)
            sb.Append(", Diff ").Append(loreRequirement.ToString(CultureInfo.InvariantCulture));

        // 24. Salvage workmanship / craft workmanship.
        if (item.ObjectClass == PluginObjectClass.Salvage)
        {
            double salvageWork = item.SalvageWorkmanship;
            if (salvageWork > 0)
                sb.Append(", Work ").Append(salvageWork.ToString("N2", CultureInfo.InvariantCulture));
        }
        else
        {
            int workmanship = item.GetInt(ItemModel.WorkmanshipKey, 0);
            if (workmanship > 0 && tinks != 10)
                sb.Append(", Craft ").Append(workmanship.ToString(CultureInfo.InvariantCulture));
        }

        // 25. Unenchantable armor protections.
        if (item.ObjectClass == PluginObjectClass.Armor && item.GetInt(ItemModel.UnenchantableKey, 0) != 0)
        {
            sb.Append(", [")
                .Append(item.GetDouble(ItemModel.SlashProtKey, 0).ToString("N1", CultureInfo.InvariantCulture))
                .Append('/')
                .Append(item.GetDouble(ItemModel.PierceProtKey, 0).ToString("N1", CultureInfo.InvariantCulture))
                .Append('/')
                .Append(item.GetDouble(ItemModel.BludgeonProtKey, 0).ToString("N1", CultureInfo.InvariantCulture))
                .Append('/')
                .Append(item.GetDouble(ItemModel.ColdProtKey, 0).ToString("N1", CultureInfo.InvariantCulture))
                .Append('/')
                .Append(item.GetDouble(ItemModel.FireProtKey, 0).ToString("N1", CultureInfo.InvariantCulture))
                .Append('/')
                .Append(item.GetDouble(ItemModel.AcidProtKey, 0).ToString("N1", CultureInfo.InvariantCulture))
                .Append('/')
                .Append(item.GetDouble(ItemModel.LightningProtKey, 0).ToString("N1", CultureInfo.InvariantCulture))
                .Append(']');
        }

        // 26. Value and burden.
        if (settings.ShowValueAndBurden)
        {
            int value = item.GetInt(ItemModel.ValueKey, 0);
            if (value > 0)
                sb.Append(", Value ").Append(value.ToString("n0", CultureInfo.InvariantCulture));

            int burden = item.GetInt(ItemModel.BurdenKey, 0);
            if (burden > 0)
                sb.Append(", BU ").Append(burden.ToString(CultureInfo.InvariantCulture));
        }

        // 27. Ratings block.
        if (item.TotalRating > 0)
            AppendRatings(sb, item);

        // 28. Keyring.
        if (item.ObjectClass == PluginObjectClass.Misc && item.Name.Contains("Keyring", StringComparison.Ordinal))
        {
            sb.Append(", Keys: ").Append(item.GetInt(ItemModel.KeysHeldKey, 0).ToString(CultureInfo.InvariantCulture))
                .Append(", Uses: ").Append(item.GetInt(ItemModel.UsesRemainingKey, 0).ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static void AppendImbueFlag(StringBuilder sb, int imbued, int bit, string token)
    {
        if ((imbued & bit) == bit)
            sb.Append(token);
    }

    private static string RoundToString(double multiplier)
        => Math.Round((multiplier - 1) * 100).ToString(CultureInfo.InvariantCulture);

    private static void AppendBuffedBlock(StringBuilder sb, ItemModel item)
    {
        sb.Append(", (");

        if (item.ObjectClass == PluginObjectClass.MeleeWeapon)
        {
            sb.Append(item.CalcedBuffedTinkedDoT.ToString("N1", CultureInfo.InvariantCulture))
                .Append('/').Append(item.BuffedMaxDamage.ToString(CultureInfo.InvariantCulture));
        }

        if (item.ObjectClass == PluginObjectClass.MissileWeapon)
            sb.Append(item.CalcedBuffedMissileDamage.ToString("N1", CultureInfo.InvariantCulture));

        if (item.ObjectClass == PluginObjectClass.WandStaffOrb)
            sb.Append(((item.BuffedElementalDamageVersusMonsters - 1) * 100).ToString("N1", CultureInfo.InvariantCulture));

        sb.Append(' ');

        if (item.AttackBonus != 1)
            sb.Append(Math.Round((item.BuffedAttackBonus - 1) * 100).ToString("N1", CultureInfo.InvariantCulture)).Append('/');

        if (item.GetDouble(ItemModel.MeleeDefenseBonusKey, 1) != 1)
            sb.Append(Math.Round((item.BuffedMeleeDefenseBonus - 1) * 100).ToString("N1", CultureInfo.InvariantCulture));

        if (item.GetDouble(ItemModel.ManaCBonusKey, 0) != 0)
            sb.Append('/').Append(Math.Round(item.BuffedManaCBonus * 100).ToString(CultureInfo.InvariantCulture));

        sb.Append(')');
    }

    /// <summary>
    /// Segment 18's spell filter chain, first match wins, in the order the
    /// original checks them. Spells are the item's own carried enchantments
    /// (<see cref="ItemModel.SpellIds"/>), sorted by id descending.
    /// </summary>
    private static void AppendSpells(StringBuilder sb, ItemModel item, ISpellCatalog spells)
    {
        if (item.SpellIds.Count == 0)
            return;

        var sorted = item.SpellIds.OrderByDescending(id => id).ToList();
        bool isLootGenerated = item.HasInt(ItemModel.MaterialKey);
        bool isUnenchantable = item.GetInt(ItemModel.UnenchantableKey, 0) != 0;

        foreach (uint spellId in sorted)
        {
            // The original indexed the spell table directly with no presence
            // check and would have thrown for an id the table doesn't have;
            // this port skips an unresolvable id instead of crashing the
            // whole line. See docs/deviations.md.
            if (!spells.TryGet(spellId, out PluginSpellInfo spell))
                continue;

            if (ShouldShowSpell(spell, isLootGenerated, isUnenchantable))
                sb.Append(", ").Append(spell.Name);
        }
    }

    private static bool ShouldShowSpell(PluginSpellInfo spell, bool isLootGenerated, bool isUnenchantable)
    {
        // Not loot generated -> show everything.
        if (!isLootGenerated)
            return true;

        // Always show Minor/Major/Epic/Legendary Impenetrability and trinket
        // (Augmented) spells.
        if (spell.Name.Contains("Minor Impenetrability", StringComparison.Ordinal)
            || spell.Name.Contains("Major Impenetrability", StringComparison.Ordinal)
            || spell.Name.Contains("Epic Impenetrability", StringComparison.Ordinal)
            || spell.Name.Contains("Legendary Impenetrability", StringComparison.Ordinal)
            || spell.Name.Contains("Augmented", StringComparison.Ordinal))
            return true;

        bool isBaneOrImpenOrBrogard =
            spell.Name.Contains(" Bane", StringComparison.Ordinal)
            || spell.Name.Contains("Impen", StringComparison.Ordinal)
            || spell.Name.StartsWith("Brogard", StringComparison.Ordinal);

        if (isUnenchantable)
        {
            if (isBaneOrImpenOrBrogard)
                return true;
        }
        else
        {
            if (isBaneOrImpenOrBrogard)
                return false;
        }

        // Weapon-buff families.
        if ((spell.Family >= 152 && spell.Family <= 158) || spell.Family == 195 || spell.Family == 325)
        {
            if (spell.Difficulty == 250) // lvl 6
                return false;
            if (spell.Difficulty == 300) // lvl 7
                return true;
            if (spell.Difficulty >= 400) // lvl 8+
                return true;
            return false;
        }

        // Not a weapon buff: filter 1-6's, 7's and Incantations (8's).
        if (spell.Name.EndsWith(" I", StringComparison.Ordinal)
            || spell.Name.EndsWith(" II", StringComparison.Ordinal)
            || spell.Name.EndsWith(" III", StringComparison.Ordinal)
            || spell.Name.EndsWith(" IV", StringComparison.Ordinal)
            || spell.Name.EndsWith(" V", StringComparison.Ordinal))
            return false;

        if (spell.Name.EndsWith(" VI", StringComparison.Ordinal))
            return false;

        if (spell.Difficulty == 300)
            return false;

        if (spell.Name.Contains("Incantation", StringComparison.Ordinal))
            return false;

        return true;
    }

    private static void AppendRatings(StringBuilder sb, ItemModel item)
    {
        sb.Append(", [");
        bool first = true;

        AppendRating(sb, ref first, item.GetInt(ItemModel.DamRatingKey, -1), "D");
        AppendRating(sb, ref first, item.GetInt(ItemModel.DamResistRatingKey, -1), "DR");
        AppendRating(sb, ref first, item.GetInt(ItemModel.CritRatingKey, -1), "C");
        AppendRating(sb, ref first, item.GetInt(ItemModel.CritDamRatingKey, -1), "CD");
        AppendRating(sb, ref first, item.GetInt(ItemModel.CritResistRatingKey, -1), "CR");
        AppendRating(sb, ref first, item.GetInt(ItemModel.CritDamResistRatingKey, -1), "CDR");
        AppendRating(sb, ref first, item.GetInt(ItemModel.HealBoostRatingKey, -1), "HB");
        AppendRating(sb, ref first, item.GetInt(ItemModel.VitalityRatingKey, -1), "V");

        sb.Append(']');
    }

    private static void AppendRating(StringBuilder sb, ref bool first, int value, string label)
    {
        if (value <= 0)
            return;
        if (!first)
            sb.Append(", ");
        sb.Append(label).Append(' ').Append(value.ToString(CultureInfo.InvariantCulture));
        first = false;
    }
}

/// <summary>The two <c>ItemInfoOnIdent</c> settings the formatter reads.</summary>
public interface ISettings
{
    bool ShowBuffedValues { get; }
    bool ShowValueAndBurden { get; }
}
