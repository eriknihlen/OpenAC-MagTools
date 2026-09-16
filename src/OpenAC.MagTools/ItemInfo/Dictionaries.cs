namespace OpenAC.MagTools.ItemInfo;

/// <summary>
/// Static lookup tables the item-info string and its buffed-value math need.
/// Ported verbatim from the original's <c>Shared/Constants/Dictionaries.cs</c>
/// (skill/mastery/attribute-set/material names) and the two spell-effect
/// tables that drive <see cref="ItemModel.GetBuffedInt"/> /
/// <see cref="ItemModel.GetBuffedDouble"/>.
/// </summary>
public static class Dictionaries
{
    /// <summary>Skill ids (the item info format's wield/activation skill names).</summary>
    public static readonly IReadOnlyDictionary<int, string> SkillInfo =
        new Dictionary<int, string>
        {
            { 0x1, "Axe" },
            { 0x2, "Bow" },
            { 0x3, "Crossbow" },
            { 0x4, "Dagger" },
            { 0x5, "Mace" },
            { 0x6, "Melee Defense" },
            { 0x7, "Missile Defense" },
            { 0x8, "Sling" },
            { 0x9, "Spear" },
            { 0xA, "Staff" },
            { 0xB, "Sword" },
            { 0xC, "Thrown Weapons" },
            { 0xD, "Unarmed Combat" },
            { 0xE, "Arcane Lore" },
            { 0xF, "Magic Defense" },
            { 0x10, "Mana Conversion" },
            { 0x12, "Item Tinkering" },
            { 0x13, "Assess Person" },
            { 0x14, "Deception" },
            { 0x15, "Healing" },
            { 0x16, "Jump" },
            { 0x17, "Lockpick" },
            { 0x18, "Run" },
            { 0x1B, "Assess Creature" },
            { 0x1C, "Weapon Tinkering" },
            { 0x1D, "Armor Tinkering" },
            { 0x1E, "Magic Item Tinkering" },
            { 0x1F, "Creature Enchantment" },
            { 0x20, "Item Enchantment" },
            { 0x21, "Life Magic" },
            { 0x22, "War Magic" },
            { 0x23, "Leadership" },
            { 0x24, "Loyalty" },
            { 0x25, "Fletching" },
            { 0x26, "Alchemy" },
            { 0x27, "Cooking" },
            { 0x28, "Salvaging" },
            { 0x29, "Two Handed Combat" },
            { 0x2A, "Gearcraft" },
            { 0x2B, "Void" },
            { 0x2C, "Heavy Weapons" },
            { 0x2D, "Light Weapons" },
            { 0x2E, "Finesse Weapons" },
            { 0x2F, "Missile Weapons" },
            { 0x30, "Shield" },
            { 0x31, "Dual Wield" },
            { 0x32, "Recklessness" },
            { 0x33, "Sneak Attack" },
            { 0x34, "Dirty Fighting" },
            { 0x35, "Challenge" },
            { 0x36, "Summoning" },
        };

    /// <summary>Weapon mastery ids (segment 3 of the item info string).</summary>
    public static readonly IReadOnlyDictionary<int, string> MasteryInfo =
        new Dictionary<int, string>
        {
            { 1, "Unarmed Weapon" },
            { 2, "Sword" },
            { 3, "Axe" },
            { 4, "Mace" },
            { 5, "Spear" },
            { 6, "Dagger" },
            { 7, "Staff" },
            { 8, "Bow" },
            { 9, "Crossbow" },
            { 10, "Thrown" },
            { 11, "Two Handed Combat" },
        };

    /// <summary>Item-set ids (segment 4 of the item info string).</summary>
    public static readonly IReadOnlyDictionary<int, string> AttributeSetInfo =
        new Dictionary<int, string>
        {
            { 02, "Test" },
            { 04, "Carraida's Benediction" },
            { 05, "Noble Relic Set" },
            { 06, "Ancient Relic Set" },
            { 07, "Relic Alduressa Set" },
            { 08, "Shou-jen Set" },
            { 09, "Empyrean Rings Set" },
            { 10, "Arm, Mind, Heart Set" },
            { 11, "Coat of the Perfect Light Set" },
            { 12, "Leggings of Perfect Light Set" },
            { 13, "Soldier's Set" },
            { 14, "Adept's Set" },
            { 15, "Archer's Set" },
            { 16, "Defender's Set" },
            { 17, "Tinker's Set" },
            { 18, "Crafter's Set" },
            { 19, "Hearty Set" },
            { 20, "Dexterous Set" },
            { 21, "Wise Set" },
            { 22, "Swift Set" },
            { 23, "Hardenend Set" },
            { 24, "Reinforced Set" },
            { 25, "Interlocking Set" },
            { 26, "Flame Proof Set" },
            { 27, "Acid Proof Set" },
            { 28, "Cold Proof Set" },
            { 29, "Lightning Proof Set" },
            { 30, "Dedication Set" },
            { 31, "Gladiatorial Clothing Set" },
            { 32, "Ceremonial Clothing" },
            { 33, "Protective Clothing" },
            { 34, "Noobie Armor" },
            { 35, "Sigil of Defense" },
            { 36, "Sigil of Destruction" },
            { 37, "Sigil of Fury" },
            { 38, "Sigil of Growth" },
            { 39, "Sigil of Vigor" },
            { 40, "Heroic Protector Set" },
            { 41, "Heroic Destroyer Set" },
            { 42, "Olthoi Armor D Red" },
            { 43, "Olthoi Armor C Rat" },
            { 44, "Olthoi Armor C Red" },
            { 45, "Olthoi Armor D Rat" },
            { 46, "Upgraded Relic Alduressa Set" },
            { 47, "Upgraded Ancient Relic Set" },
            { 48, "Upgraded Noble Relic Set" },
            { 49, "Weave of Alchemy" },
            { 50, "Weave of Arcane Lore" },
            { 51, "Weave of Armor Tinkering" },
            { 52, "Weave of Assess Person" },
            { 53, "Weave of Light Weapons" },
            { 54, "Weave of Missile Weapons" },
            { 55, "Weave of Cooking" },
            { 56, "Weave of Creature Enchantment" },
            { 57, "Weave of Missile Weapons" },
            { 58, "Weave of Finesse" },
            { 59, "Weave of Deception" },
            { 60, "Weave of Fletching" },
            { 61, "Weave of Healing" },
            { 62, "Weave of Item Enchantment" },
            { 63, "Weave of Item Tinkering" },
            { 64, "Weave of Leadership" },
            { 65, "Weave of Life Magic" },
            { 66, "Weave of Loyalty" },
            { 67, "Weave of Light Weapons" },
            { 68, "Weave of Magic Defense" },
            { 69, "Weave of Magic Item Tinkering" },
            { 70, "Weave of Mana Conversion" },
            { 71, "Weave of Melee Defense" },
            { 72, "Weave of Missile Defense" },
            { 73, "Weave of Salvaging" },
            { 74, "Weave of Light Weapons" },
            { 75, "Weave of Light Weapons" },
            { 76, "Weave of Heavy Weapons" },
            { 77, "Weave of Missile Weapons" },
            { 78, "Weave of Two Handed Combat" },
            { 79, "Weave of Light Weapons" },
            { 80, "Weave of Void Magic" },
            { 81, "Weave of War Magic" },
            { 82, "Weave of Weapon Tinkering" },
            { 83, "Weave of Assess Creature " },
            { 84, "Weave of Dirty Fighting" },
            { 85, "Weave of Dual Wield" },
            { 86, "Weave of Recklessness" },
            { 87, "Weave of Shield" },
            { 88, "Weave of Sneak Attack" },
            { 89, "Ninja_New" },
            { 90, "Weave of Summoning" },
            { 91, "Shrouded Soul" },
            { 92, "Darkened Mind" },
            { 93, "Clouded Spirit" },
            { 94, "Minor Stinging Shrouded Soul" },
            { 95, "Minor Sparking Shrouded Soul" },
            { 96, "Minor Smoldering Shrouded Soul" },
            { 97, "Minor Shivering Shrouded Soul" },
            { 98, "Minor Stinging Darkened Mind" },
            { 99, "Minor Sparking Darkened Mind" },
            { 100, "Minor Smoldering Darkened Mind" },
            { 101, "Minor Shivering Darkened Mind" },
            { 102, "Minor Stinging Clouded Spirit" },
            { 103, "Minor Sparking Clouded Spirit" },
            { 104, "Minor Smoldering Clouded Spirit" },
            { 105, "Minor Shivering Clouded Spirit" },
            { 106, "Major Stinging Shrouded Soul" },
            { 107, "Major Sparking Shrouded Soul" },
            { 108, "Major Smoldering Shrouded Soul" },
            { 109, "Major Shivering Shrouded Soul" },
            { 110, "Major Stinging Darkened Mind" },
            { 111, "Major Sparking Darkened Mind" },
            { 112, "Major Smoldering Darkened Mind" },
            { 113, "Major Shivering Darkened Mind" },
            { 114, "Major Stinging Clouded Spirit" },
            { 115, "Major Sparking Clouded Spirit" },
            { 116, "Major Smoldering Clouded Spirit" },
            { 117, "Major Shivering Clouded Spirit" },
            { 118, "Blackfire Stinging Shrouded Soul" },
            { 119, "Blackfire Sparking Shrouded Soul" },
            { 120, "Blackfire Smoldering Shrouded Soul" },
            { 121, "Blackfire Shivering Shrouded Soul" },
            { 122, "Blackfire Stinging Darkened Mind" },
            { 123, "Blackfire Sparking Darkened Mind" },
            { 124, "Blackfire Smoldering Darkened Mind" },
            { 125, "Blackfire Shivering Darkened Mind" },
            { 126, "Blackfire Stinging Clouded Spirit" },
            { 127, "Blackfire Sparking Clouded Spirit" },
            { 128, "Blackfire Smoldering Clouded Spirit" },
            { 129, "Blackfire Shivering Clouded Spirit" },
            { 130, "Shimmering Shadows" },
            { 131, "Brown Society Locket" },
            { 132, "Yellow Society Locket" },
            { 133, "Red Society Band" },
            { 134, "Green Society Band" },
            { 135, "Purple Society Band" },
            { 136, "Blue Society Band" },
            { 137, "Gauntlet Garb" },
            { 138, "UNKNOWN_138" },
            { 139, "UNKNOWN_139" },
            { 140, "UNKNOWN_140" },
        };

    /// <summary>Loot-generated item material ids (segment 1 of the item info string).</summary>
    public static readonly IReadOnlyDictionary<int, string> MaterialInfo =
        new Dictionary<int, string>
        {
            { 1, "Ceramic" },
            { 2, "Porcelain" },
            { 4, "Linen" },
            { 5, "Satin" },
            { 6, "Silk" },
            { 7, "Velvet" },
            { 8, "Wool" },
            { 10, "Agate" },
            { 11, "Amber" },
            { 12, "Amethyst" },
            { 13, "Aquamarine" },
            { 14, "Azurite" },
            { 15, "Black Garnet" },
            { 16, "Black Opal" },
            { 17, "Bloodstone" },
            { 18, "Carnelian" },
            { 19, "Citrine" },
            { 20, "Diamond" },
            { 21, "Emerald" },
            { 22, "Fire Opal" },
            { 23, "Green Garnet" },
            { 24, "Green Jade" },
            { 25, "Hematite" },
            { 26, "Imperial Topaz" },
            { 27, "Jet" },
            { 28, "Lapis Lazuli" },
            { 29, "Lavender Jade" },
            { 30, "Malachite" },
            { 31, "Moonstone" },
            { 32, "Onyx" },
            { 33, "Opal" },
            { 34, "Peridot" },
            { 35, "Red Garnet" },
            { 36, "Red Jade" },
            { 37, "Rose Quartz" },
            { 38, "Ruby" },
            { 39, "Sapphire" },
            { 40, "Smokey Quartz" },
            { 41, "Sunstone" },
            { 42, "Tiger Eye" },
            { 43, "Tourmaline" },
            { 44, "Turquoise" },
            { 45, "White Jade" },
            { 46, "White Quartz" },
            { 47, "White Sapphire" },
            { 48, "Yellow Garnet" },
            { 49, "Yellow Topaz" },
            { 50, "Zircon" },
            { 51, "Ivory" },
            { 52, "Leather" },
            { 53, "Armoredillo Hide" },
            { 54, "Gromnie Hide" },
            { 55, "Reed Shark Hide" },
            { 57, "Brass" },
            { 58, "Bronze" },
            { 59, "Copper" },
            { 60, "Gold" },
            { 61, "Iron" },
            { 62, "Pyreal" },
            { 63, "Silver" },
            { 64, "Steel" },
            { 66, "Alabaster" },
            { 67, "Granite" },
            { 68, "Marble" },
            { 69, "Obsidian" },
            { 70, "Sandstone" },
            { 71, "Serpentine" },
            { 73, "Ebony" },
            { 74, "Mahogany" },
            { 75, "Oak" },
            { 76, "Pine" },
            { 77, "Teak" },
        };

    /// <summary>One item-property spell effect: what it changes (subtracted while active, or
    /// added as a bonus when the item itself carries the spell) on the item's raw property.</summary>
    public readonly record struct SpellEffect<T>(int Key, T Change, T Bonus = default!);

    // Raw property ids the effect tables key off of (see ItemModel's own
    // constants for the full property-id cross-reference).
    private const int MaxDamageKey = 218103842;
    private const int ArmorLevelKey = 28;
    private const int ElementalDamageVersusMonstersKey = 152;
    private const int AttackBonusKey = 167772172;
    private const int MeleeDefenseBonusKey = 29;
    private const int ManaCBonusKey = 144;

    /// <summary>Int-valued property effects (max damage, armor level) from item spells.</summary>
    public static readonly IReadOnlyDictionary<int, SpellEffect<int>> LongValueKeySpellEffects =
        new Dictionary<int, SpellEffect<int>>
        {
            // Removed in 2012 and converted to player auras; kept here because
            // some legacy items still carry them.
            { 1616, new SpellEffect<int>(MaxDamageKey, 20) }, // Blood Drinker VI
            { 2096, new SpellEffect<int>(MaxDamageKey, 22) }, // Infected Caress
            { 5183, new SpellEffect<int>(MaxDamageKey, 24) }, // Incantation of Blood Drinker (post Feb-2013)
            { 4395, new SpellEffect<int>(MaxDamageKey, 24) }, // Incantation of Blood Drinker (post Feb-2013)

            { 2598, new SpellEffect<int>(MaxDamageKey, 2, 2) }, // Minor Blood Thirst
            { 2586, new SpellEffect<int>(MaxDamageKey, 4, 4) }, // Major Blood Thirst
            { 4661, new SpellEffect<int>(MaxDamageKey, 7, 7) }, // Epic Blood Thirst
            { 6089, new SpellEffect<int>(MaxDamageKey, 10, 10) }, // Legendary Blood Thirst

            { 3688, new SpellEffect<int>(MaxDamageKey, 300) }, // Prodigal Blood Drinker

            { 1486, new SpellEffect<int>(ArmorLevelKey, 200) }, // Impenetrability VI
            { 2108, new SpellEffect<int>(ArmorLevelKey, 220) }, // Brogard's Defiance
            { 4407, new SpellEffect<int>(ArmorLevelKey, 240) }, // Incantation of Impenetrability

            { 2604, new SpellEffect<int>(ArmorLevelKey, 20, 20) }, // Minor Impenetrability
            { 2592, new SpellEffect<int>(ArmorLevelKey, 40, 40) }, // Major Impenetrability
            { 4667, new SpellEffect<int>(ArmorLevelKey, 60, 60) }, // Epic Impenetrability
            { 6095, new SpellEffect<int>(ArmorLevelKey, 80, 80) }, // Legendary Impenetrability
        };

    /// <summary>Double-valued property effects (elemental %, attack bonus, defense bonus,
    /// mana conversion bonus) from item spells.</summary>
    public static readonly IReadOnlyDictionary<int, SpellEffect<double>> DoubleValueKeySpellEffects =
        new Dictionary<int, SpellEffect<double>>
        {
            { 3258, new SpellEffect<double>(ElementalDamageVersusMonstersKey, .06) }, // Spirit Drinker VI
            { 3259, new SpellEffect<double>(ElementalDamageVersusMonstersKey, .07) }, // Infected Spirit Caress
            { 5182, new SpellEffect<double>(ElementalDamageVersusMonstersKey, .08) }, // Incantation of Spirit Drinker (post Feb-2013)
            { 4414, new SpellEffect<double>(ElementalDamageVersusMonstersKey, .08) }, // Incantation of Spirit Drinker (post Feb-2013)

            { 3251, new SpellEffect<double>(ElementalDamageVersusMonstersKey, .01, .01) }, // Minor Spirit Thirst
            { 3250, new SpellEffect<double>(ElementalDamageVersusMonstersKey, .03, .03) }, // Major Spirit Thirst
            { 4670, new SpellEffect<double>(ElementalDamageVersusMonstersKey, .05, .05) }, // Epic Spirit Thirst
            { 6098, new SpellEffect<double>(ElementalDamageVersusMonstersKey, .07, .07) }, // Legendary Spirit Thirst

            { 3735, new SpellEffect<double>(ElementalDamageVersusMonstersKey, .15) }, // Prodigal Spirit Drinker

            { 1592, new SpellEffect<double>(AttackBonusKey, .15) }, // Heart Seeker VI
            { 2106, new SpellEffect<double>(AttackBonusKey, .17) }, // Elysa's Sight
            { 4405, new SpellEffect<double>(AttackBonusKey, .20) }, // Incantation of Heart Seeker

            { 2603, new SpellEffect<double>(AttackBonusKey, .03, .03) }, // Minor Heart Thirst
            { 2591, new SpellEffect<double>(AttackBonusKey, .05, .05) }, // Major Heart Thirst
            { 4666, new SpellEffect<double>(AttackBonusKey, .07, .07) }, // Epic Heart Thirst
            { 6094, new SpellEffect<double>(AttackBonusKey, .09, .09) }, // Legendary Heart Thirst

            { 1605, new SpellEffect<double>(MeleeDefenseBonusKey, .15) }, // Defender VI
            { 2101, new SpellEffect<double>(MeleeDefenseBonusKey, .17) }, // Cragstone's Will
            { 4400, new SpellEffect<double>(MeleeDefenseBonusKey, .20) }, // Incantation of Defender (post Feb-2013)

            { 2600, new SpellEffect<double>(MeleeDefenseBonusKey, .03, .03) }, // Minor Defender
            { 3985, new SpellEffect<double>(MeleeDefenseBonusKey, .04, .04) }, // Mukkir Sense
            { 2588, new SpellEffect<double>(MeleeDefenseBonusKey, .05, .05) }, // Major Defender
            { 4663, new SpellEffect<double>(MeleeDefenseBonusKey, .07, .07) }, // Epic Defender
            { 6091, new SpellEffect<double>(MeleeDefenseBonusKey, .09, .09) }, // Legendary Defender

            { 3699, new SpellEffect<double>(MeleeDefenseBonusKey, .25) }, // Prodigal Defender

            { 1480, new SpellEffect<double>(ManaCBonusKey, 1.60) }, // Hermetic Link VI
            { 2117, new SpellEffect<double>(ManaCBonusKey, 1.70) }, // Mystic's Blessing
            { 4418, new SpellEffect<double>(ManaCBonusKey, 1.80) }, // Incantation of Hermetic Link

            { 3201, new SpellEffect<double>(ManaCBonusKey, 1.05, 1.05) }, // Feeble Hermetic Link
            { 3199, new SpellEffect<double>(ManaCBonusKey, 1.10, 1.10) }, // Minor Hermetic Link
            { 3202, new SpellEffect<double>(ManaCBonusKey, 1.15, 1.15) }, // Moderate Hermetic Link
            { 3200, new SpellEffect<double>(ManaCBonusKey, 1.20, 1.20) }, // Major Hermetic Link
            { 6086, new SpellEffect<double>(ManaCBonusKey, 1.25, 1.25) }, // Epic Hermetic Link
            { 6087, new SpellEffect<double>(ManaCBonusKey, 1.30, 1.30) }, // Legendary Hermetic Link
        };
}
