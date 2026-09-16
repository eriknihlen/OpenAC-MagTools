using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;

namespace OpenAC.MagTools.Tests.ItemInfo;

public sealed class ItemModelTests
{
    [Fact]
    public void MaxDamageResolvesFromTheTypedInventoryField()
    {
        var wo = ItemInfoFixtures.Wo(1, "Sword", PluginObjectClass.MeleeWeapon);
        var inv = ItemInfoFixtures.Inv(1, "Sword", PluginObjectClass.MeleeWeapon, damage: 42);
        var model = new ItemModel(wo, ItemInfoFixtures.Props(), inv);

        Assert.Equal(42, model.MaxDamage);
        Assert.True(model.HasInt(218103842));
    }

    [Fact]
    public void MaxDamageDefaultsToZeroWithoutAnInventoryItem()
    {
        // Matches the format's own `!= 0` checks, not MyWorldObject's -1
        // "absent" sentinel — see the remark on ItemModel.MaxDamage.
        var wo = ItemInfoFixtures.Wo(1, "Sword", PluginObjectClass.MeleeWeapon);
        var model = new ItemModel(wo, ItemInfoFixtures.Props());

        Assert.Equal(0, model.MaxDamage);
        Assert.False(model.HasInt(218103842));
    }

    [Fact]
    public void MaxDamageAndEquipSkillFallBackToTheRealPropertyIdsForLandscapeItems()
    {
        // A landscape/vendor object has captured properties but no
        // PluginInventoryItem (it isn't owned or in an open container).
        // Damage (44) and WeaponSkill (48) should still resolve off the
        // property bag so these items print damage/skill correctly.
        var wo = ItemInfoFixtures.Wo(1, "Sword", PluginObjectClass.MeleeWeapon);
        var model = new ItemModel(
            wo,
            ItemInfoFixtures.Props(ints: new Dictionary<uint, int> { { 44, 37 }, { 48, 0xB } }));

        Assert.True(model.HasInt(218103842));
        Assert.Equal(37, model.MaxDamage);
        Assert.True(model.HasInt(218103840));
        Assert.Equal(0xB, model.EquipSkillId);
    }

    [Fact]
    public void VarianceFallsBackToTheRealPropertyIdForLandscapeItems()
    {
        var wo = ItemInfoFixtures.Wo(1, "Sword", PluginObjectClass.MeleeWeapon);
        var model = new ItemModel(
            wo,
            ItemInfoFixtures.Props(floats: new Dictionary<uint, double> { { 22, 0.25 } }));

        Assert.True(model.HasDouble(167772171));
        Assert.Equal(0.25, model.Variance);
    }

    [Fact]
    public void CalcedBuffedTinkedDoTReturnsMinusOneWhenDamageIsAbsent()
    {
        // Like the original: no Damage/Variance data at all (a landscape
        // item with no property for either) -> the DoT calc bails to -1
        // instead of running the greedy tink simulation on garbage.
        var wo = ItemInfoFixtures.Wo(1, "Sword", PluginObjectClass.MeleeWeapon);
        var model = new ItemModel(wo, ItemInfoFixtures.Props());

        Assert.Equal(-1, model.CalcedBuffedTinkedDoT);
    }

    [Fact]
    public void VarianceAndSalvageWorkmanshipResolveFromTypedFields()
    {
        var wo = ItemInfoFixtures.Wo(1, "Bag", PluginObjectClass.Salvage);
        var inv = ItemInfoFixtures.Inv(1, "Bag", PluginObjectClass.Salvage, variance: 0.35, workmanship: 3f);
        var model = new ItemModel(wo, ItemInfoFixtures.Props(), inv);

        Assert.Equal(0.35, model.Variance);
        Assert.Equal(3d, model.SalvageWorkmanship);
    }

    [Fact]
    public void EquipSkillResolvesFromTheWeaponSkillField()
    {
        var wo = ItemInfoFixtures.Wo(1, "Sword", PluginObjectClass.MeleeWeapon);
        var inv = ItemInfoFixtures.Inv(1, "Sword", PluginObjectClass.MeleeWeapon, weaponSkill: 0xB);
        var model = new ItemModel(wo, ItemInfoFixtures.Props(), inv);

        Assert.Equal(0xB, model.EquipSkillId);
        Assert.Equal("Sword", ItemModel.SkillName(model.EquipSkillId));
    }

    [Fact]
    public void AttackBonusAndDamageBonusResolveFromRealPropertyIds()
    {
        var wo = ItemInfoFixtures.Wo(1, "Sword", PluginObjectClass.MeleeWeapon);
        var model = ItemInfoFixtures.Model(wo, floats: new Dictionary<uint, double>
        {
            { 62, 1.2 }, // WeaponOffense -> AttackBonus pseudo-key
            { 63, 1.1 }, // DamageMod -> DamageBonus pseudo-key
        });

        Assert.Equal(1.2, model.AttackBonus);
        Assert.Equal(1.1, model.DamageBonus);
    }

    [Fact]
    public void AttackBonusDefaultsToOneWhenAbsent()
    {
        var wo = ItemInfoFixtures.Wo(1, "Sword", PluginObjectClass.MeleeWeapon);
        var model = ItemInfoFixtures.Model(wo);

        Assert.Equal(1d, model.AttackBonus);
        Assert.Equal(1d, model.DamageBonus);
    }

    [Fact]
    public void MaterialNameFallsBackToUnknownMaterial()
    {
        var wo = ItemInfoFixtures.Wo(1, "Thing", PluginObjectClass.Misc);
        var known = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int> { { 131, 61 } });
        var unknown = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int> { { 131, 9999 } });
        var none = ItemInfoFixtures.Model(wo);

        Assert.Equal("Iron", known.MaterialName);
        Assert.Equal("unknown material 9999", unknown.MaterialName);
        Assert.Null(none.MaterialName);
    }

    [Fact]
    public void GetBuffedIntSubtractsActiveEffectsAndAddsCarriedBonus()
    {
        // 2598 = Minor Blood Thirst: MaxDamage Change 2, Bonus 2.
        var wo = ItemInfoFixtures.Wo(
            1, "Sword", PluginObjectClass.MeleeWeapon,
            spellIds: [2598],
            activeSpellIds: [2598]);
        var inv = ItemInfoFixtures.Inv(1, "Sword", PluginObjectClass.MeleeWeapon, damage: 20);
        var model = new ItemModel(wo, ItemInfoFixtures.Props(), inv);

        // Raw 20, minus the active-effect Change (2), plus the carried-spell Bonus (2) -> 20.
        Assert.Equal(20, model.BuffedMaxDamage);
    }

    [Fact]
    public void GetBuffedIntAppliesOnlyTheCarriedBonusWhenNotActive()
    {
        var wo = ItemInfoFixtures.Wo(1, "Sword", PluginObjectClass.MeleeWeapon, spellIds: [2598]);
        var inv = ItemInfoFixtures.Inv(1, "Sword", PluginObjectClass.MeleeWeapon, damage: 20);
        var model = new ItemModel(wo, ItemInfoFixtures.Props(), inv);

        Assert.Equal(22, model.BuffedMaxDamage);
    }

    [Fact]
    public void GetBuffedDoubleAppliesAdditiveChangeWhenNotExactlyOne()
    {
        // 3251 = Minor Spirit Thirst: ElementalDamageVersusMonsters Change .01, Bonus .01.
        var wo = ItemInfoFixtures.Wo(1, "Wand", PluginObjectClass.WandStaffOrb, activeSpellIds: [3251]);
        var model = ItemInfoFixtures.Model(wo, floats: new Dictionary<uint, double> { { 152, 1.10 } });

        Assert.Equal(1.09, model.BuffedElementalDamageVersusMonsters, precision: 6);
    }

    [Fact]
    public void GetBuffedDoubleCarriedBonusAppliesAdditivelyWhenChangeIsNotExactlyOne()
    {
        // Every entry in the ported spell-effect table has Change != 1 (the
        // "divide by 1" branch documented on GetBuffedDouble is therefore
        // unreachable through any real spell — an upstream quirk carried
        // forward verbatim, not exercised here because nothing in the table
        // can trigger it). This pins the additive branch every real entry
        // actually takes: 3199 = Minor Hermetic Link, ManaCBonus Change 1.10.
        var wo = ItemInfoFixtures.Wo(1, "Wand", PluginObjectClass.WandStaffOrb, spellIds: [3199]);
        var model = ItemInfoFixtures.Model(wo, floats: new Dictionary<uint, double> { { 144, 2.0 } });

        Assert.Equal(3.10, model.BuffedManaCBonus, precision: 6);
    }

    [Fact]
    public void CalculateDamageOverTimeMatchesTheOriginalFormula()
    {
        double result = ItemModel.CalculateDamageOverTime(100, 0.2);
        // 100 * ((1-.1)*(2-.2)/2 + .1*2) = 100 * (0.81 + 0.2) = 101.0
        Assert.Equal(101.0, result, precision: 6);
    }

    [Fact]
    public void TotalRatingSumsOnlyPositiveRatingsAndIsMinusOneWhenNoneExist()
    {
        var wo = ItemInfoFixtures.Wo(1, "Ring", PluginObjectClass.Jewelry);
        var withRatings = ItemInfoFixtures.Model(
            wo, ints: new Dictionary<uint, int> { { 370, 5 }, { 372, 3 } });
        var none = ItemInfoFixtures.Model(wo);

        Assert.Equal(8, withRatings.TotalRating);
        Assert.Equal(-1, none.TotalRating);
    }
}
