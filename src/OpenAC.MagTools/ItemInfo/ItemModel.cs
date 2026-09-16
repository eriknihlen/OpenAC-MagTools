using System.Globalization;
using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.ItemInfo;

/// <summary>
/// A view over a host <see cref="PluginWorldObject"/>/<see cref="PluginItemProperties"/>
/// pair that gives the original <c>Shared/MyWorldObject.cs</c>'s typed accessors
/// and buffed-value math, without a private copy of the property bag.
/// </summary>
/// <remarks>
/// <para>
/// Property keys below are the raw AC property ids, verbatim from the
/// original's Decal value-key numbers (confirmed against
/// <c>src/AcDream.Core/Properties/PropertyInt.cs</c> and
/// <c>PropertyFloat.cs</c> in the frozen A2 snapshot — the numbers are
/// identical; only the C# enum member names differ from Decal's).
/// </para>
/// <para>
/// Several keys are Decal-only pseudo-properties, not real AC property ids
/// (the <c>0x0A000000</c>/<c>0x0D000000</c>-prefixed numbers the port design
/// calls out). <see cref="GetInt"/>/<see cref="GetDouble"/> (and their
/// <c>Has*</c> counterparts) intercept these numbers and resolve them off
/// the host's typed record fields when one is present (an owned/open-
/// container item), falling back to the matching real property id read off
/// the captured property bag otherwise (a landscape/vendor item, which has
/// properties but no <see cref="PluginInventoryItem"/>) — so every other
/// reader in this class (including the buffed-value math, which keys its
/// spell-effect tables off these exact numbers) can use them exactly like
/// any other property key. Confirmed against the retained
/// <c>Mag.Shared.Constants.IntValueKey.ConvertToInt</c> /
/// <c>DoubleValueKey.ConvertToDouble</c> tables:
/// </para>
/// <list type="table">
/// <item><term>218103842 (MaxDamage)</term><description><see cref="PluginInventoryItem.Damage"/>, else <c>Ints[44]</c> (ACE's <c>Damage</c>)</description></item>
/// <item><term>218103840 (EquipSkill)</term><description><see cref="PluginInventoryItem.WeaponSkill"/>, else <c>Ints[48]</c> (ACE's <c>WeaponSkill</c>)</description></item>
/// <item><term>167772171 (Variance)</term><description><see cref="PluginInventoryItem.DamageVariance"/>, else <c>Floats[22]</c> (ACE's <c>DamageVariance</c>)</description></item>
/// <item><term>167772169 (SalvageWorkmanship)</term><description><see cref="PluginInventoryItem.Workmanship"/> only — <c>ConvertToDouble</c> has no real-property mapping for this one</description></item>
/// <item><term>167772172 (AttackBonus)</term><description>real property id 62 (ACE's <c>WeaponOffense</c>)</description></item>
/// <item><term>167772174 (DamageBonus)</term><description>real property id 63 (ACE's <c>DamageMod</c>)</description></item>
/// </list>
/// </remarks>
public sealed class ItemModel
{
    /// <summary>
    /// An empty property bag for when the host has none to offer (an object
    /// with no captured appraisal data). <c>default(PluginItemProperties)</c>
    /// has null dictionaries, not empty ones, so callers that fall back to it
    /// must use this instead — every accessor here assumes non-null maps.
    /// </summary>
    public static readonly PluginItemProperties EmptyProperties = new(
        new Dictionary<uint, int>(),
        new Dictionary<uint, long>(),
        new Dictionary<uint, bool>(),
        new Dictionary<uint, double>(),
        new Dictionary<uint, string>(),
        new Dictionary<uint, uint>(),
        new Dictionary<uint, uint>());

    // ---- raw property ids ----------------------------------------------------
    public const int NameKey = 1;
    public const int BurdenKey = 5;
    public const int ValueKey = 19;
    public const int ArmorLevelKey = 28;
    public const int MeleeDefenseBonusKey = 29;
    // The seven armour-protection floats. Decal's SlashProt..LightningProt
    // pseudo-keys convert (via the retained DoubleValueKey.ConvertToDouble
    // table) to ACE's ArmorModVsSlash/Pierce/Bludgeon/Cold/Fire/Acid/Electric
    // — floats 13-19, NOT the 64-70 creature-resistance ints those numbers
    // would otherwise suggest.
    public const int SlashProtKey = 13;
    public const int PierceProtKey = 14;
    public const int BludgeonProtKey = 15;
    public const int ColdProtKey = 16;
    public const int FireProtKey = 17;
    public const int AcidProtKey = 18;
    public const int LightningProtKey = 19;
    public const int WorkmanshipKey = 105;
    public const int LoreRequirementKey = 109;
    public const int MaterialKey = 131;
    public const int ManaCBonusKey = 144;
    public const int MissileDBonusKey = 149;
    public const int MagicDBonusKey = 150;
    public const int ElementalDamageVersusMonstersKey = 152;
    public const int WieldReqTypeKey = 158;
    public const int WieldReqAttributeKey = 159;
    public const int WieldReqValueKey = 160;
    public const int NumberTimesTinkeredKey = 171;
    public const int ImbuedKey = 179;
    public const int ElementalDmgBonusKey = 204;
    public const int AttributeSetKey = 265;
    public const int MasteryKey = 353;
    public const int UseRequiresSkillKey = 366;
    public const int UseRequiresSkillLevelKey = 367;
    public const int UseRequiresSkillSpecializedKey = 368;
    public const int SummoningGemLevelKey = 369;
    public const int DamRatingKey = 370;
    public const int DamResistRatingKey = 371;
    public const int CritRatingKey = 372;
    public const int CritResistRatingKey = 373;
    public const int CritDamRatingKey = 374;
    public const int CritDamResistRatingKey = 375;
    public const int HealBoostRatingKey = 376;
    public const int VitalityRatingKey = 379;
    // Unenchantable/KeysHeld/UsesRemaining have no entry in the retained
    // IntValueKey.ConvertToInt table (they're real, non-pseudo Decal
    // property numbers, not 0x0A000000-prefixed pseudo-keys), so they're
    // taken directly from Decal.Adapter.Wrappers.LongValueKey's own metadata
    // (extracted from Decal.Adapter.dll 2.9.7.5 and 2.9.8.3, both v11
    // client SDKs; identical in both). The host's AcDream.Core.Properties
    // enums use different member names for these three ids than Decal does
    // (e.g. host id 92 is PropertyInt.Structure, not "UsesRemaining") — the
    // NUMBER is what matters for wire compatibility, not the label; ACE
    // repurposed/renamed a few of Decal's older property slots over time.
    // NumKeys (193) is the one exception with a matching host member name.
    public const int UnenchantableKey = 36;
    public const int KeysHeldKey = 193; // host: PropertyInt.NumKeys
    public const int UsesRemainingKey = 92; // host: PropertyInt.Structure (same id, different ACE-era name)

    /// <summary>
    /// The activation-skill pairing (segment 21, "Skill N to Activate").
    /// Confirmed as ACE's <c>PropertyDataId.ItemSkillLimit</c>
    /// (37) — a <em>DataId</em>, not an Int property, unlike every other key
    /// in this class. <see cref="GetInt"/>/<see cref="HasInt"/> intercept
    /// this key and read <c>DataIds[37]</c> instead of <c>Ints[37]</c>.
    /// </summary>
    public const int ActivationReqSkillIdKey = 37;

    /// <summary>
    /// The activation skill LEVEL (segment 21). Confirmed as ACE's
    /// <c>ItemSkillLevelLimit</c> (115).
    /// </summary>
    public const int SkillLevelReqKey = 115;

    // ---- Decal-only pseudo-property ids (see class remarks) ------------------
    private const int MaxDamagePseudoKey = 218103842;
    private const int EquipSkillPseudoKey = 218103840;
    private const int VariancePseudoKey = 167772171;
    private const int SalvageWorkmanshipPseudoKey = 167772169;
    private const int AttackBonusPseudoKey = 167772172;
    private const int DamageBonusPseudoKey = 167772174;

    /// <summary>
    /// ACE's <c>Damage</c> — the real property MaxDamage falls back to.
    /// Confirmed via <c>IntValueKey.ConvertToInt</c>. Internal (not
    /// <c>private</c>) so <see cref="ItemInfoPrinter"/> can build a synthetic
    /// classifier item for landscape/vendor objects off the same id.
    /// </summary>
    internal const int DamageRealKey = 44;

    /// <summary>ACE's <c>WeaponSkill</c> — the real property EquipSkill falls back to. Confirmed via <c>IntValueKey.ConvertToInt</c>.</summary>
    internal const int WeaponSkillRealKey = 48;

    /// <summary>ACE's <c>DamageVariance</c> — the real property Variance falls back to. Confirmed via <c>DoubleValueKey.ConvertToDouble</c>.</summary>
    internal const int DamageVarianceRealKey = 22;

    /// <summary>ACE's <c>WeaponOffense</c> — the real property AttackBonus resolves to. Confirmed via <c>DoubleValueKey.ConvertToDouble</c>.</summary>
    private const int AttackBonusRealKey = 62;

    /// <summary>ACE's <c>DamageMod</c> — the real property DamageBonus resolves to. Confirmed via <c>DoubleValueKey.ConvertToDouble</c>.</summary>
    private const int DamageBonusRealKey = 63;

    private readonly PluginWorldObject _worldObject;
    private readonly PluginItemProperties _properties;
    private readonly PluginInventoryItem? _inventoryItem;

    public ItemModel(PluginWorldObject worldObject, PluginItemProperties properties)
        : this(worldObject, properties, null)
    {
    }

    public ItemModel(
        PluginWorldObject worldObject,
        PluginItemProperties properties,
        PluginInventoryItem? inventoryItem)
    {
        _worldObject = worldObject;
        _properties = properties;
        _inventoryItem = inventoryItem;
    }

    public uint ObjectId => _worldObject.ObjectId;
    public string Name => _worldObject.Name;
    public PluginObjectClass ObjectClass => _worldObject.ObjectClass;
    public bool HasIdData => _worldObject.HasAppraisalData;
    public IReadOnlyList<uint> ActiveSpellIds => _worldObject.ActiveSpellIds;
    public IReadOnlyList<uint> SpellIds => _worldObject.SpellIds;

    // ---- raw accessors, matching MyWorldObject.Values(key, default) ---------
    // These intercept the Decal pseudo-keys (see class remarks) so every
    // other reader below — including the buffed-value math — can treat them
    // like any other property key. MaxDamage/EquipSkill/Variance prefer the
    // host's typed inventory field when one exists (an owned/open-container
    // item), and otherwise fall back to the real property id read straight
    // off the captured property bag — so a landscape/vendor item (properties
    // only, no PluginInventoryItem) still reports its damage/skill/variance
    // instead of always reading as absent.

    public bool HasInt(int key) => key switch
    {
        MaxDamagePseudoKey => _inventoryItem.HasValue || _properties.Ints.ContainsKey((uint)DamageRealKey),
        EquipSkillPseudoKey => _inventoryItem.HasValue || _properties.Ints.ContainsKey((uint)WeaponSkillRealKey),
        ActivationReqSkillIdKey => _properties.DataIds.ContainsKey((uint)key),
        _ => _properties.Ints.ContainsKey((uint)key),
    };

    public int GetInt(int key, int defaultValue = -1) => key switch
    {
        MaxDamagePseudoKey => _inventoryItem?.Damage
            ?? (_properties.Ints.TryGetValue((uint)DamageRealKey, out int dmg) ? dmg : defaultValue),
        EquipSkillPseudoKey => _inventoryItem?.WeaponSkill
            ?? (_properties.Ints.TryGetValue((uint)WeaponSkillRealKey, out int skill) ? skill : defaultValue),
        // ActivationReqSkillId (37) is ACE's PropertyDataId.ItemSkillLimit — a
        // DataId, not an Int property; read the DataIds map instead of Ints.
        ActivationReqSkillIdKey =>
            _properties.DataIds.TryGetValue((uint)key, out uint activationSkillId)
                ? (int)activationSkillId
                : defaultValue,
        _ => _properties.Ints.TryGetValue((uint)key, out int value) ? value : defaultValue,
    };

    public bool HasDouble(int key) => key switch
    {
        VariancePseudoKey => _inventoryItem.HasValue || _properties.Floats.ContainsKey((uint)DamageVarianceRealKey),
        // SalvageWorkmanship has no real-property fallback in the retained
        // ConvertToDouble table — it only ever comes from the typed field.
        SalvageWorkmanshipPseudoKey => _inventoryItem.HasValue,
        AttackBonusPseudoKey => _properties.Floats.ContainsKey((uint)AttackBonusRealKey),
        DamageBonusPseudoKey => _properties.Floats.ContainsKey((uint)DamageBonusRealKey),
        _ => _properties.Floats.ContainsKey((uint)key),
    };

    public double GetDouble(int key, double defaultValue = -1d) => key switch
    {
        VariancePseudoKey => _inventoryItem?.DamageVariance
            ?? (_properties.Floats.TryGetValue((uint)DamageVarianceRealKey, out double variance) ? variance : defaultValue),
        SalvageWorkmanshipPseudoKey => _inventoryItem?.Workmanship ?? defaultValue,
        AttackBonusPseudoKey =>
            _properties.Floats.TryGetValue((uint)AttackBonusRealKey, out double a) ? a : defaultValue,
        DamageBonusPseudoKey =>
            _properties.Floats.TryGetValue((uint)DamageBonusRealKey, out double d) ? d : defaultValue,
        _ => _properties.Floats.TryGetValue((uint)key, out double value) ? value : defaultValue,
    };

    public bool HasBool(int key) => _properties.Bools.ContainsKey((uint)key);

    public bool GetBool(int key, bool defaultValue = false)
        => _properties.Bools.TryGetValue((uint)key, out bool value) ? value : defaultValue;

    // ---- the Decal-only pseudo-properties, by name ---------------------------

    // Default 0, not -1: the format's segment 8 checks these directly
    // against 0 (`wo.Values(MaxDamage) != 0`), the same way the original
    // read the raw Decal WorldObject rather than through MyWorldObject's own
    // "-1 means absent" convention.
    public int MaxDamage => GetInt(MaxDamagePseudoKey, 0);

    public double Variance => GetDouble(VariancePseudoKey, 0d);

    public double SalvageWorkmanship => GetDouble(SalvageWorkmanshipPseudoKey, -1d);

    public int EquipSkillId => GetInt(EquipSkillPseudoKey, -1);

    public double AttackBonus => GetDouble(AttackBonusPseudoKey, 1d);

    public double DamageBonus => GetDouble(DamageBonusPseudoKey, 1d);

    // ---- named lookups (segments 1/3/4/23/wield/activation) ------------------

    public string? MaterialName
    {
        get
        {
            int material = GetInt(MaterialKey, 0);
            if (material <= 0)
                return null;
            return Dictionaries.MaterialInfo.TryGetValue(material, out string? name)
                ? name
                : "unknown material " + material.ToString(CultureInfo.InvariantCulture);
        }
    }

    public string? MasteryName
    {
        get
        {
            int mastery = GetInt(MasteryKey, 0);
            if (mastery <= 0)
                return null;
            return Dictionaries.MasteryInfo.TryGetValue(mastery, out string? name)
                ? name
                : "Unknown mastery " + mastery.ToString(CultureInfo.InvariantCulture);
        }
    }

    public string? AttributeSetName
    {
        get
        {
            int set = GetInt(AttributeSetKey, 0);
            if (set == 0)
                return null;
            return Dictionaries.AttributeSetInfo.TryGetValue(set, out string? name)
                ? name
                : "Unknown set " + set.ToString(CultureInfo.InvariantCulture);
        }
    }

    public static string SkillName(int skillId)
        => Dictionaries.SkillInfo.TryGetValue(skillId, out string? name)
            ? name
            : "Unknown skill: " + skillId.ToString(CultureInfo.InvariantCulture);

    // ---- buffed-value math, ported verbatim from MyWorldObject.cs -----------

    /// <summary>
    /// <c>GetBuffedIntValueKey</c>: the raw value, less the <c>Change</c> of
    /// every active spell that touches <paramref name="key"/>, plus the
    /// <c>Bonus</c> of every spell the item itself carries that touches it.
    /// </summary>
    public int GetBuffedInt(int key, int defaultValue = 0)
    {
        if (!HasInt(key))
            return defaultValue;

        int value = GetInt(key);

        foreach (uint spell in ActiveSpellIds)
        {
            if (Dictionaries.LongValueKeySpellEffects.TryGetValue((int)spell, out var effect)
                && effect.Key == key)
                value -= effect.Change;
        }

        foreach (uint spell in SpellIds)
        {
            if (Dictionaries.LongValueKeySpellEffects.TryGetValue((int)spell, out var effect)
                && effect.Key == key)
                value += effect.Bonus;
        }

        return value;
    }

    /// <summary>
    /// <c>GetBuffedDoubleValueKey</c>, ported bug-for-bug: an active effect
    /// whose <c>Change</c> is exactly 1 divides by that 1 (a no-op) instead of
    /// applying anything, exactly as the original does — only an active
    /// effect with <c>Change != 1</c> actually reduces the buffed value. The
    /// item's own carried-spell <c>Bonus</c> half is unaffected by this and
    /// applies normally (multiplicative when <c>Change == 1</c>, additive
    /// otherwise).
    /// </summary>
    public double GetBuffedDouble(int key, double defaultValue = 0d)
    {
        if (!HasDouble(key))
            return defaultValue;

        double value = GetDouble(key);

        foreach (uint spell in ActiveSpellIds)
        {
            if (!Dictionaries.DoubleValueKeySpellEffects.TryGetValue((int)spell, out var effect)
                || effect.Key != key)
                continue;

            if (Math.Abs(effect.Change - 1d) < double.Epsilon)
                value /= effect.Change; // ported as-is; see remarks
            else
                value -= effect.Change;
        }

        foreach (uint spell in SpellIds)
        {
            if (!Dictionaries.DoubleValueKeySpellEffects.TryGetValue((int)spell, out var effect)
                || effect.Key != key
                || Math.Abs(effect.Bonus - 0d) <= double.Epsilon)
                continue;

            if (Math.Abs(effect.Change - 1d) < double.Epsilon)
                value *= effect.Bonus;
            else
                value += effect.Bonus;
        }

        return value;
    }

    /// <summary><c>CalculateDamageOverTime</c>, verbatim.</summary>
    public static double CalculateDamageOverTime(
        int maxDamage,
        double variance,
        double critChance = .1,
        double critMultiplier = 2)
        => maxDamage * ((1 - critChance) * (2 - variance) / 2 + critChance * critMultiplier);

    private int Tinks => GetInt(NumberTimesTinkeredKey, -1);

    /// <summary><c>CalcedBuffedTinkedDoT</c>, verbatim greedy iron/granite tink simulation.</summary>
    public double CalcedBuffedTinkedDoT
    {
        get
        {
            if (!HasDouble(VariancePseudoKey) || !HasInt(MaxDamagePseudoKey))
                return -1;

            double variance = GetDouble(VariancePseudoKey, 0);
            int maxDamage = GetBuffedInt(MaxDamagePseudoKey);

            int numberOfTinksLeft = Math.Max(10 - Math.Max(Tinks, 0), 0);

            if (GetInt(ImbuedKey, 0) == 0)
                numberOfTinksLeft--; // factor in an imbue tink

            if (GetInt(MaterialKey, 0) == 0)
                numberOfTinksLeft = 0; // not loot generated, can't be tinked

            for (int i = 1; i <= numberOfTinksLeft; i++)
            {
                double ironTinkDoT = CalculateDamageOverTime(maxDamage + 24 + 1, variance);
                double graniteTinkDoT = CalculateDamageOverTime(maxDamage + 24, variance * .8);

                if (ironTinkDoT >= graniteTinkDoT)
                    maxDamage++;
                else
                    variance *= .8;
            }

            return CalculateDamageOverTime(maxDamage + 24, variance);
        }
    }

    /// <summary><c>CalcedBuffedMissileDamage</c>, verbatim.</summary>
    public double CalcedBuffedMissileDamage
    {
        get
        {
            if (!HasInt(MaxDamagePseudoKey) || !HasDouble(DamageBonusPseudoKey)
                || !HasInt(ElementalDmgBonusKey))
                return -1;

            return GetBuffedInt(MaxDamagePseudoKey)
                + (GetBuffedDouble(DamageBonusPseudoKey) - 1) * 100 / 3
                + GetBuffedInt(ElementalDmgBonusKey);
        }
    }

    /// <summary>The buffed max damage — used by the melee buffed-block segment.</summary>
    public int BuffedMaxDamage => GetBuffedInt(MaxDamagePseudoKey);

    public double BuffedElementalDamageVersusMonsters
        => GetBuffedDouble(ElementalDamageVersusMonstersKey, -1);

    public double BuffedAttackBonus => GetBuffedDouble(AttackBonusPseudoKey, -1);

    public double BuffedMeleeDefenseBonus => GetBuffedDouble(MeleeDefenseBonusKey, -1);

    public double BuffedManaCBonus => GetBuffedDouble(ManaCBonusKey, -1);

    public int TotalRating
    {
        get
        {
            int dam = GetInt(DamRatingKey, -1);
            int damResist = GetInt(DamResistRatingKey, -1);
            int crit = GetInt(CritRatingKey, -1);
            int critResist = GetInt(CritResistRatingKey, -1);
            int critDam = GetInt(CritDamRatingKey, -1);
            int critDamResist = GetInt(CritDamResistRatingKey, -1);
            int healBoost = GetInt(HealBoostRatingKey, -1);
            int vitality = GetInt(VitalityRatingKey, -1);

            if (dam == -1 && damResist == -1 && crit == -1 && critResist == -1
                && critDam == -1 && critDamResist == -1 && healBoost == -1 && vitality == -1)
                return -1;

            return Math.Max(dam, 0) + Math.Max(damResist, 0) + Math.Max(crit, 0)
                + Math.Max(critResist, 0) + Math.Max(critDam, 0) + Math.Max(critDamResist, 0)
                + Math.Max(healBoost, 0) + Math.Max(vitality, 0);
        }
    }
}
