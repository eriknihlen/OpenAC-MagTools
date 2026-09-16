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

        var unenchantableWo = ItemInfoFixtures.Wo(1, "Ring", PluginObjectClass.Jewelry, spellIds: [20, 21]);
        var unenchantable = ItemInfoFixtures.Model(
            unenchantableWo,
            ints: new Dictionary<uint, int> { { 131, 61 }, { 415, 1 } });
        Assert.Equal(
            "Iron Ring, Brogard's Ward, Some Bane",
            ItemInfoFormatter.Format(unenchantable, DefaultSettings, spells));
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
        var wo = ItemInfoFixtures.Wo(1, "Plate", PluginObjectClass.Armor);
        var model = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int> { { 415, 1 } },
            floats: new Dictionary<uint, double>
            {
                { 64, 1 }, { 65, 2 }, { 66, 3 }, { 68, 4 }, { 67, 5 }, { 69, 6 }, { 70, 7 },
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
        var wo = ItemInfoFixtures.Wo(1, "Burning Sands Keyring", PluginObjectClass.Misc);
        var model = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int> { { 417, 12 }, { 418, 4 } });

        Assert.Equal(
            "Burning Sands Keyring, Keys: 12, Uses: 4",
            ItemInfoFormatter.Format(model, DefaultSettings, EmptySpells));
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
