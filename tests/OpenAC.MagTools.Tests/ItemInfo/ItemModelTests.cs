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
    public void MaterialNameFallsBackToBareNumber()
    {
        // Original: `Dictionaries.MaterialInfo.ContainsKey(...) ? ... :
        // IntValues[131].ToString(...)` — an unmapped id renders as the bare
        // number, not a synthesized "unknown material N" string.
        var wo = ItemInfoFixtures.Wo(1, "Thing", PluginObjectClass.Misc);
        var known = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int> { { 131, 61 } });
        var unknown = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int> { { 131, 9999 } });
        var none = ItemInfoFixtures.Model(wo);

        Assert.Equal("Iron", known.MaterialName);
        Assert.Equal("9999", unknown.MaterialName);
        Assert.Null(none.MaterialName);
    }

    [Fact]
    public void MasteryNameFallsBackToBareNumber()
    {
        // Regression for the live-gate "(80)" observation: property 353 = 80
        // on a real appraisal (outside the original's 1-11 weapon-mastery
        // range) must render as the bare number "80", matching the
        // original's `IntValues[353].ToString(...)` fallback exactly — not a
        // synthesized "Unknown mastery 80" string.
        var wo = ItemInfoFixtures.Wo(1, "Acid Phyntos Wasp Essence", PluginObjectClass.Misc);
        var known = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int> { { 353, 2 } });
        var unknown = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int> { { 353, 80 } });

        Assert.Equal("Sword", known.MasteryName);
        Assert.Equal("80", unknown.MasteryName);
    }

    [Fact]
    public void AttributeSetNameFallsBackToBareNumber()
    {
        var wo = ItemInfoFixtures.Wo(1, "Thing", PluginObjectClass.Misc);
        var unknown = ItemInfoFixtures.Model(wo, ints: new Dictionary<uint, int> { { 265, 999999 } });

        Assert.Equal("999999", unknown.AttributeSetName);
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

    // ---- P3 follow-up: WeaponProfile / ArmorProfile precedence --------------

    [Fact]
    public void WeaponProfileWinsOverTheTypedInventoryFieldAndThePropertyTable()
    {
        var wo = ItemInfoFixtures.Wo(1, "War Mace", PluginObjectClass.MeleeWeapon);
        var inv = ItemInfoFixtures.Inv(1, "War Mace", PluginObjectClass.MeleeWeapon, damage: 5, variance: 0.1, weaponSkill: 4);
        var profile = new PluginWeaponProfile(
            DamageType: 4, WeaponTime: 0, WeaponSkill: 5, Damage: 60, DamageVariance: 0.3,
            DamageMod: 1.15, WeaponLength: 0, MaxVelocity: 0, WeaponOffense: 1.2, MaxVelocityEstimated: 0);
        var model = ItemInfoFixtures.Model(
            wo,
            ints: new Dictionary<uint, int> { { 44, 1 }, { 48, 1 } },
            floats: new Dictionary<uint, double> { { 62, 2d }, { 63, 2d } },
            inv: inv,
            weaponProfile: profile);

        Assert.Equal(60, model.MaxDamage);
        Assert.Equal(0.3, model.Variance);
        Assert.Equal(5, model.EquipSkillId);
        Assert.Equal(1.2, model.AttackBonus);
        Assert.Equal(1.15, model.DamageBonus);
    }

    [Fact]
    public void WeaponProfileDamageOfMinusOneIsUnknownAndFallsThroughToTheTypedField()
    {
        // Per docs/plugin-api.md: Damage == -1 inside a present profile means
        // "unknown", not "absent profile" — MaxDamage specifically falls
        // through, but other fields on the same profile (WeaponOffense) stay
        // authoritative.
        var wo = ItemInfoFixtures.Wo(1, "Odd Weapon", PluginObjectClass.MeleeWeapon);
        var inv = ItemInfoFixtures.Inv(1, "Odd Weapon", PluginObjectClass.MeleeWeapon, damage: 12, weaponSkill: 4);
        var profile = new PluginWeaponProfile(
            DamageType: 4, WeaponTime: 0, WeaponSkill: 0, Damage: -1, DamageVariance: 0.25,
            DamageMod: 1.05, WeaponLength: 0, MaxVelocity: 0, WeaponOffense: 1.1, MaxVelocityEstimated: 0);
        var model = ItemInfoFixtures.Model(wo, inv: inv, weaponProfile: profile);

        Assert.Equal(12, model.MaxDamage);
        Assert.Equal(0.25, model.Variance);
        Assert.Equal(1.1, model.AttackBonus);
    }

    [Fact]
    public void ArmorProfileSuppliesTheSevenProtectionsAndExcludesNether()
    {
        var wo = ItemInfoFixtures.Wo(1, "Plate", PluginObjectClass.Armor);
        var profile = new PluginArmorProfile(
            ArmorLevel: 200,
            SlashMod: 1.1f, PierceMod: 1.2f, BludgeonMod: 1.3f, ColdMod: 0.9f,
            FireMod: 0.8f, AcidMod: 0.7f, NetherMod: 5.0f, ElectricMod: 0.6f);
        var model = ItemInfoFixtures.Model(wo, armorProfile: profile);

        // ArmorProfile's mods are float; comparing at float precision avoids
        // a spurious failure from the widening to double.
        Assert.Equal(1.1, model.GetDouble(ItemModel.SlashProtKey), precision: 5);
        Assert.Equal(1.2, model.GetDouble(ItemModel.PierceProtKey), precision: 5);
        Assert.Equal(1.3, model.GetDouble(ItemModel.BludgeonProtKey), precision: 5);
        Assert.Equal(0.9, model.GetDouble(ItemModel.ColdProtKey), precision: 5);
        Assert.Equal(0.8, model.GetDouble(ItemModel.FireProtKey), precision: 5);
        Assert.Equal(0.7, model.GetDouble(ItemModel.AcidProtKey), precision: 5);
        // Electric (lightning) comes off ArmorProfile.ElectricMod, not NetherMod.
        Assert.Equal(0.6, model.GetDouble(ItemModel.LightningProtKey), precision: 5);
    }

    [Fact]
    public void ArmorProfileProtectionsFallBackToThePropertyTableWhenAbsent()
    {
        var wo = ItemInfoFixtures.Wo(1, "Old Plate", PluginObjectClass.Armor);
        var model = ItemInfoFixtures.Model(
            wo,
            floats: new Dictionary<uint, double>
            {
                { 13, 1.0 }, { 14, 1.0 }, { 15, 1.0 }, { 16, 1.0 }, { 17, 1.0 }, { 18, 1.0 }, { 19, 1.0 },
            });

        Assert.Equal(1.0, model.GetDouble(ItemModel.SlashProtKey));
        Assert.Equal(1.0, model.GetDouble(ItemModel.LightningProtKey));
    }
}
