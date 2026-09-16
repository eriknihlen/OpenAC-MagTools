using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;

namespace OpenAC.MagTools.Tests.ItemInfo;

public sealed class ItemInfoFormatterTests
{
    private static readonly FakeItemInfoSettings DefaultSettings = new();
    private static readonly FakeFormatterSpellCatalog EmptySpells = new();

    [Fact]
    public void MaterialNameAndMasteryAndSetPrefixTheLine()
    {
        // Armor, not a weapon class, so the buffed block (segment 17) never
        // applies here regardless of the ShowBuffedValues setting.
        var wo = ItemInfoFixtures.Wo(1, "Long Sword", PluginObjectClass.Armor);
        var model = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int>
        {
            { 131, 61 }, // Iron
            { 353, 2 },  // Sword mastery
            { 265, 13 }, // Soldier's Set
        });

        string result = ItemInfoFormatter.Format(model, DefaultSettings, EmptySpells);

        Assert.Equal("Iron Long Sword (Sword), Soldier's Set", result);
    }

    [Fact]
    public void ArmorLevelAndImbueFlagsAndTinks()
    {
        var wo = ItemInfoFixtures.Wo(1, "Breastplate", PluginObjectClass.Armor);
        var model = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int>
        {
            { 28, 200 },
            { 179, 1 | 8 | 8192 }, // CS + SlashRend + Hematited
            { 171, 6 },
        });

        string result = ItemInfoFormatter.Format(model, DefaultSettings, EmptySpells);

        Assert.Equal("Breastplate, AL 200, CS SlashRend Hematited, Tinks 6", result);
    }

    [Fact]
    public void DamageSegmentShowsRangeWhenVarianceIsNonZero()
    {
        var wo = ItemInfoFixtures.Wo(1, "Sword", PluginObjectClass.MeleeWeapon);
        var inv = ItemInfoFixtures.Inv(1, "Sword", PluginObjectClass.MeleeWeapon, damage: 50, variance: 0.2);
        var model = new ItemModel(wo, ItemInfoFixtures.Props(), inv);

        string result = ItemInfoFormatter.Format(
            model, new FakeItemInfoSettings { ShowBuffedValues = false }, EmptySpells);

        Assert.Equal("Sword, 40.00-50", result);
    }

    [Fact]
    public void DamageSegmentShowsPlainMaxDamageWhenVarianceIsZero()
    {
        var wo = ItemInfoFixtures.Wo(1, "Sword", PluginObjectClass.MeleeWeapon);
        var inv = ItemInfoFixtures.Inv(1, "Sword", PluginObjectClass.MeleeWeapon, damage: 50, variance: 0);
        var model = new ItemModel(wo, ItemInfoFixtures.Props(), inv);

        string result = ItemInfoFormatter.Format(
            model, new FakeItemInfoSettings { ShowBuffedValues = false }, EmptySpells);

        Assert.Equal("Sword, 50", result);
    }

    [Fact]
    public void PercentageBonusSegmentsFormatInOrder()
    {
        var wo = ItemInfoFixtures.Wo(1, "Sword", PluginObjectClass.MeleeWeapon);
        var model = ItemInfoFixtures.Model(
            wo,
            ints: new Dictionary<uint, int> { { 204, 5 } },
            floats: new Dictionary<uint, double>
            {
                { 63, 1.10 },  // DamageBonus real key -> +10%
                { 152, 1.20 }, // vs monsters -> +20%vs. Monsters
                { 62, 1.05 },  // AttackBonus real key -> +5%a
                { 29, 1.15 },  // MeleeDefenseBonus -> 15%md
                { 150, 1.021 },// MagicDBonus -> 2.1%mgc.d
                { 149, 0.90 }, // MissileDBonus -> -10.0%msl.d
                { 144, 0.5 },  // ManaCBonus -> 50%mc
            });

        string result = ItemInfoFormatter.Format(
            model, new FakeItemInfoSettings { ShowBuffedValues = false }, EmptySpells);

        Assert.Equal(
            "Sword, +5, +10%, +20%vs. Monsters, +5%a, 15%md, 2.1%mgc.d, -10%msl.d, 50%mc",
            result);
    }

    [Fact]
    public void BuffedBlockOnlyAppearsForWeaponClassesWhenEnabled()
    {
        var meleeWo = ItemInfoFixtures.Wo(1, "Sword", PluginObjectClass.MeleeWeapon);
        var meleeInv = ItemInfoFixtures.Inv(1, "Sword", PluginObjectClass.MeleeWeapon, damage: 50, variance: 0.2);
        var meleeModel = new ItemModel(meleeWo, ItemInfoFixtures.Props(
            ints: new Dictionary<uint, int> { { 131, 61 } }), meleeInv);

        string withBuffed = ItemInfoFormatter.Format(meleeModel, DefaultSettings, EmptySpells);
        string withoutBuffed = ItemInfoFormatter.Format(
            meleeModel, new FakeItemInfoSettings { ShowBuffedValues = false }, EmptySpells);

        Assert.Contains(", (", withBuffed);
        Assert.DoesNotContain(", (", withoutBuffed);

        var miscWo = ItemInfoFixtures.Wo(1, "Widget", PluginObjectClass.Misc);
        var miscModel = ItemInfoFixtures.Model(miscWo);
        Assert.DoesNotContain(", (", ItemInfoFormatter.Format(miscModel, DefaultSettings, EmptySpells));
    }

    [Fact]
    public void NonLootGeneratedItemShowsEverySpellRegardlessOfFilter()
    {
        var spells = new FakeFormatterSpellCatalog();
        spells.Add(100, "Some VI Spell"); // would normally be filtered (ends " VI")
        spells.Add(50, "Incantation of Something", family: 999, difficulty: 300); // would normally be filtered

        var wo = ItemInfoFixtures.Wo(1, "Trinket", PluginObjectClass.Misc, spellIds: [100, 50]);
        var model = ItemInfoFixtures.Model(wo); // no Material key -> not loot generated

        string result = ItemInfoFormatter.Format(model, DefaultSettings, spells);

        // Sorted by id descending: 100 then 50.
        Assert.Equal("Trinket, Some VI Spell, Incantation of Something", result);
    }

    [Fact]
    public void LootGeneratedItemFiltersTieredSpellsButKeepsImpenAndAugmented()
    {
        var spells = new FakeFormatterSpellCatalog();
        spells.Add(10, "Minor Strength"); // not I-VI tiered by name here, ends "Strength" - keep
        spells.Add(11, "Major Strength III"); // ends " III" -> filtered
        spells.Add(12, "Legendary Impenetrability"); // always shown
        spells.Add(13, "Augmented Awareness"); // trinket spell, always shown
        spells.Add(14, "Some Incantation of Foo"); // contains "Incantation" -> filtered (not weapon-buff family)

        var wo = ItemInfoFixtures.Wo(1, "Ring", PluginObjectClass.Jewelry, spellIds: [10, 11, 12, 13, 14]);
        var model = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int> { { 131, 61 } });

        string result = ItemInfoFormatter.Format(model, DefaultSettings, spells);

        // Sorted by id descending (13, 12, 11 filtered, 10): Augmented (13)
        // before Impenetrability (12) before the plain-name spell (10).
        Assert.Equal("Iron Ring, Augmented Awareness, Legendary Impenetrability, Minor Strength", result);
    }

    [Fact]
    public void UnenchantableItemShowsBaneAndImpenSpellsInsteadOfHidingThem()
    {
        var spells = new FakeFormatterSpellCatalog();
        spells.Add(20, "Some Bane");
        spells.Add(21, "Brogard's Ward");

        var enchantableWo = ItemInfoFixtures.Wo(1, "Ring", PluginObjectClass.Jewelry, spellIds: [20, 21]);
        var enchantable = ItemInfoFixtures.Model(enchantableWo, ints: new Dictionary<uint, int> { { 131, 61 } });
        Assert.Equal("Iron Ring", ItemInfoFormatter.Format(enchantable, DefaultSettings, spells));

        // 36 = Unenchantable, confirmed via Decal.Adapter.Wrappers.LongValueKey
        // metadata (see ItemModel.cs remarks) — not the fabricated 415.
        var unenchantableWo = ItemInfoFixtures.Wo(1, "Ring", PluginObjectClass.Jewelry, spellIds: [20, 21]);
        var unenchantable = ItemInfoFixtures.Model(
            unenchantableWo,
            ints: new Dictionary<uint, int> { { 131, 61 }, { 36, 1 } });
        Assert.Equal(
            "Iron Ring, Brogard's Ward, Some Bane",
            ItemInfoFormatter.Format(unenchantable, DefaultSettings, spells));
    }

    [Fact]
    public void UnenchantableLootGearStillShowsBanesAndImpen()
    {
        // The inverted branch: enchantable (no Material -> not loot
        // generated, or Material present but Unenchantable == 0) HIDES
        // banes/impen; unenchantable loot gear SHOWS them. Both arms here
        // are loot generated (Material present) to isolate the Unenchantable
        // flag itself as the variable under test.
        var spells = new FakeFormatterSpellCatalog();
        // Plain "Impenetrability" (no tier prefix) hits the isBaneOrImpen
        // branch but NOT the always-show "Minor/Major/Epic/Legendary
        // Impenetrability" literal list, so it actually exercises the
        // Unenchantable-gated branch instead of always showing.
        spells.Add(30, "Impenetrability");

        var wo = ItemInfoFixtures.Wo(1, "Buckler", PluginObjectClass.Armor, spellIds: [30]);

        var enchantableLoot = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int> { { 131, 61 } });
        Assert.DoesNotContain("Impenetrability", ItemInfoFormatter.Format(enchantableLoot, DefaultSettings, spells));

        var unenchantableLoot = ItemInfoFixtures.Model(
            wo, ints: new Dictionary<uint, int> { { 131, 61 }, { 36, 1 } });
        Assert.Contains("Impenetrability", ItemInfoFormatter.Format(unenchantableLoot, DefaultSettings, spells));
    }

    [Theory]
    [InlineData(250, false)] // lvl 6 hidden
    [InlineData(300, true)]  // lvl 7 shown
    [InlineData(400, true)]  // lvl 8+ shown
    public void WeaponBuffFamiliesFilterByDifficulty(int difficulty, bool shown)
    {
        var spells = new FakeFormatterSpellCatalog();
        spells.Add(30, "Weapon Buff Spell", family: 154, difficulty: difficulty);

        var wo = ItemInfoFixtures.Wo(1, "Sword", PluginObjectClass.MeleeWeapon, spellIds: [30]);
        var model = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int> { { 131, 61 } });

        string result = ItemInfoFormatter.Format(
            model, new FakeItemInfoSettings { ShowBuffedValues = false }, spells);

        Assert.Equal(shown, result.Contains("Weapon Buff Spell", StringComparison.Ordinal));
    }

    [Fact]
    public void WieldRequirementFormatsAsLevelForAttributeTypeSeven()
    {
        var wo = ItemInfoFixtures.Wo(1, "Sword", PluginObjectClass.MeleeWeapon);
        var levelModel = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int>
        {
            { 158, 7 }, { 159, 1 }, { 160, 180 },
        });
        var skillModel = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int>
        {
            { 158, 1 }, { 159, 0xB }, { 160, 250 },
        });

        var noBuffed = new FakeItemInfoSettings { ShowBuffedValues = false };
        Assert.Equal("Sword, Wield Lvl 180", ItemInfoFormatter.Format(levelModel, noBuffed, EmptySpells));
        Assert.Equal("Sword, Sword 250", ItemInfoFormatter.Format(skillModel, noBuffed, EmptySpells));
    }

    [Fact]
    public void SalvageWorkmanshipUsesTwoDecimalsAndCraftUsesRawInt()
    {
        var salvageWo = ItemInfoFixtures.Wo(1, "Salvage Bag", PluginObjectClass.Salvage);
        var salvageInv = ItemInfoFixtures.Inv(1, "Salvage Bag", PluginObjectClass.Salvage, workmanship: 4f);
        var salvageModel = new ItemModel(salvageWo, ItemInfoFixtures.Props(), salvageInv);
        Assert.Equal(
            "Salvage Bag, Work 4.00",
            ItemInfoFormatter.Format(salvageModel, DefaultSettings, EmptySpells));

        var noBuffed = new FakeItemInfoSettings { ShowBuffedValues = false };
        var weaponWo = ItemInfoFixtures.Wo(1, "Sword", PluginObjectClass.MeleeWeapon);
        var weaponModel = ItemInfoFixtures.Model(weaponWo, ints: new Dictionary<uint, int> { { 105, 8 } });
        Assert.Equal(
            "Sword, Craft 8",
            ItemInfoFormatter.Format(weaponModel, noBuffed, EmptySpells));

        var tenTinkedModel = ItemInfoFixtures.Model(
            weaponWo, ints: new Dictionary<uint, int> { { 105, 8 }, { 171, 10 } });
        Assert.Equal(
            "Sword, Tinks 10",
            ItemInfoFormatter.Format(tenTinkedModel, noBuffed, EmptySpells));
    }

    [Fact]
    public void UnenchantableArmorShowsTheSevenProtectionValues()
    {
        // 36 = Unenchantable (confirmed via Decal LongValueKey metadata, not
        // the fabricated 415). Protections are floats 13-19 (ACE's
        // ArmorModVsSlash/Pierce/Bludgeon/Cold/Fire/Acid/Electric, confirmed
        // via DoubleValueKey.ConvertToDouble), not the 64-70 creature
        // resistances the old constants pointed at.
        var wo = ItemInfoFixtures.Wo(1, "Plate", PluginObjectClass.Armor);
        var model = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int> { { 36, 1 } },
            floats: new Dictionary<uint, double>
            {
                { 13, 1 }, { 14, 2 }, { 15, 3 }, { 16, 4 }, { 17, 5 }, { 18, 6 }, { 19, 7 },
            });

        Assert.Equal(
            "Plate, [1.0/2.0/3.0/4.0/5.0/6.0/7.0]",
            ItemInfoFormatter.Format(model, DefaultSettings, EmptySpells));
    }

    [Fact]
    public void ValueAndBurdenOnlyPrintWhenTheSettingIsOn()
    {
        var wo = ItemInfoFixtures.Wo(1, "Coin", PluginObjectClass.Misc);
        var model = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int> { { 19, 12345 }, { 5, 3 } });

        Assert.Equal("Coin", ItemInfoFormatter.Format(model, DefaultSettings, EmptySpells));
        Assert.Equal(
            "Coin, Value 12,345, BU 3",
            ItemInfoFormatter.Format(model, new FakeItemInfoSettings { ShowValueAndBurden = true }, EmptySpells));
    }

    [Fact]
    public void RatingsBlockListsOnlyPresentRatingsInOrder()
    {
        var wo = ItemInfoFixtures.Wo(1, "Ring", PluginObjectClass.Jewelry);
        var model = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int>
        {
            { 370, 5 }, { 372, 3 }, { 376, 2 },
        });

        Assert.Equal("Ring, [D 5, C 3, HB 2]", ItemInfoFormatter.Format(model, DefaultSettings, EmptySpells));
    }

    [Fact]
    public void KeyringShowsKeysAndUsesWhenNameContainsKeyring()
    {
        // 193 = KeysHeld (host: PropertyInt.NumKeys), 92 = UsesRemaining
        // (host: PropertyInt.Structure, same id different ACE-era name) —
        // both confirmed via Decal LongValueKey metadata, not the fabricated
        // 417/418.
        var wo = ItemInfoFixtures.Wo(1, "Burning Sands Keyring", PluginObjectClass.Misc);
        var model = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int> { { 193, 12 }, { 92, 4 } });

        Assert.Equal(
            "Burning Sands Keyring, Keys: 12, Uses: 4",
            ItemInfoFormatter.Format(model, DefaultSettings, EmptySpells));
    }

    [Fact]
    public void ActivationRequirementReadsSkillIdFromTheOrdinaryIntProperty()
    {
        // ActivationReqSkillId (176) is Decal LongValueKey.ActivationReqSkillId
        // = ACE's PropertyInt.AppraisalItemSkill — an ORDINARY Int property,
        // not a DataId (a prior fix round mis-mapped it to DataId 37;
        // Decal.Adapter.dll metadata confirms 176). SkillLevelReq (115) is
        // ACE's ItemSkillLevelLimit, also an ordinary Int.
        var wo = ItemInfoFixtures.Wo(1, "Wand", PluginObjectClass.WandStaffOrb);
        var model = ItemInfoFixtures.Model(
            wo,
            ints: new Dictionary<uint, int> { { 115, 300 }, { 176, 0x36 } }); // SkillLevelReq, Summoning

        string result = ItemInfoFormatter.Format(
            model, new FakeItemInfoSettings { ShowBuffedValues = false }, EmptySpells);

        Assert.Equal("Wand, Summoning 300 to Activate", result);
    }

    [Fact]
    public void ActivationRequirementIsSuppressedWhenWieldMatchesAndCovers()
    {
        // wieldReqAttribute == activationSkillId AND the wield value already
        // meets the activation level -> segment 21 doesn't fire.
        var wo = ItemInfoFixtures.Wo(1, "Wand", PluginObjectClass.WandStaffOrb);
        var model = ItemInfoFixtures.Model(
            wo,
            ints: new Dictionary<uint, int> { { 115, 300 }, { 159, 0x36 }, { 160, 300 }, { 176, 0x36 } });

        string result = ItemInfoFormatter.Format(
            model, new FakeItemInfoSettings { ShowBuffedValues = false }, EmptySpells);

        Assert.DoesNotContain("to Activate", result);
    }

    [Fact]
    public void UnknownSkillSpecUsesTheOriginalsDistinctLiteral()
    {
        // Unknown skill spec (368) gets its own literal, "Unknown skill
        // spec: N M" — NOT the generic "Spec " + "Unknown skill: N" + " M"
        // that ItemModel.SkillName's fallback would otherwise produce.
        var wo = ItemInfoFixtures.Wo(1, "Orb", PluginObjectClass.WandStaffOrb);
        var model = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int>
        {
            { 366, 0x36 }, { 367, 5 }, // known use-requires-skill (Summoning), level 5
            { 368, 9999 }, // unknown spec id
        });

        string result = ItemInfoFormatter.Format(
            model, new FakeItemInfoSettings { ShowBuffedValues = false }, EmptySpells);

        Assert.Equal("Orb, Summoning 5, Unknown skill spec: 9999 5", result);
    }

    [Fact]
    public void ManaConversionKeyLoreAndActivationSegments()
    {
        var wo = ItemInfoFixtures.Wo(1, "Orb", PluginObjectClass.WandStaffOrb);
        var model = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int>
        {
            { 369, 3 },   // summoning gem level
            { 109, 250 }, // lore requirement
            { 366, 0x36 }, { 367, 5 }, // use-requires-skill (Summoning), level 5
            { 368, 0x36 }, // spec
        });

        Assert.Equal(
            "Orb, Lvl 3, Summoning 5, Spec Summoning 5, Diff 250",
            ItemInfoFormatter.Format(model, new FakeItemInfoSettings { ShowBuffedValues = false }, EmptySpells));
    }
}
