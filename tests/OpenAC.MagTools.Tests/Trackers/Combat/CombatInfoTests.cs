using OpenAC.MagTools.Trackers.Combat;

namespace OpenAC.MagTools.Tests.Trackers.Combat;

public sealed class CombatInfoTests
{
    [Fact]
    public void NormalHitsAccumulateIntoTheNormalBucketAndCritsIntoTheCritBucket()
    {
        var info = new CombatInfo("Me", "Drudge");

        info.AddFromCombatEventArgs(Hit(10, isCrit: false));
        info.AddFromCombatEventArgs(Hit(20, isCrit: false));
        info.AddFromCombatEventArgs(Hit(100, isCrit: true));

        Assert.Equal(3, info.TotalAttacks);
        Assert.Equal(1, info.Crits);
        Assert.Equal(30, info.TotalNormalDamage);
        Assert.Equal(20, info.MaxNormalDamage);
        Assert.Equal(100, info.TotalCritDamage);
        Assert.Equal(100, info.MaxCritDamage);
        Assert.Equal(130, info.Damage);
    }

    [Fact]
    public void FailedAttacksCountTowardTotalAttacksButNotDamage()
    {
        var info = new CombatInfo("Me", "Drudge");

        info.AddFromCombatEventArgs(new CombatEventArgs(
            "Me", "Drudge", AttackType.MeleeMissle, DamageElement.Unknown,
            IsFailedAttack: true, IsCriticalHit: false, IsOverpower: false,
            IsSneakAttack: false, IsRecklessness: false, IsKillingBlow: false, DamageAmount: 0));

        Assert.Equal(1, info.TotalAttacks);
        Assert.Equal(1, info.FailedAttacks);
        Assert.Equal(0, info.Damage);
    }

    [Fact]
    public void KillingBlowsAccumulateOnTheInfoNotThePerElementBucket()
    {
        var info = new CombatInfo("Me", "Drudge");
        info.AddFromCombatEventArgs(Hit(50, isCrit: false));
        info.AddFromCombatEventArgs(new CombatEventArgs(
            "Me", "Drudge", AttackType.Unknown, DamageElement.Unknown,
            IsFailedAttack: false, IsCriticalHit: false, IsOverpower: false,
            IsSneakAttack: false, IsRecklessness: false, IsKillingBlow: true, DamageAmount: 0));

        Assert.Equal(1, info.KillingBlows);
        // The killing-blow event itself still lands in the Unknown/Unknown
        // bucket, contributing an attack with zero damage — matching the
        // original's unconditional CombatInfo.AddFromCombatEventArgs call.
        Assert.Equal(2, info.TotalAttacks);
    }

    [Fact]
    public void SeparateAttackTypesAndElementsStayInSeparateBuckets()
    {
        var info = new CombatInfo("Me", "Drudge");
        info.AddFromCombatEventArgs(new CombatEventArgs(
            "Me", "Drudge", AttackType.MeleeMissle, DamageElement.Slash,
            false, false, false, false, false, false, 10));
        info.AddFromCombatEventArgs(new CombatEventArgs(
            "Me", "Drudge", AttackType.Magic, DamageElement.Fire,
            false, false, false, false, false, false, 20));

        DamageByAttackType melee = info.DamageByAttackTypes[AttackType.MeleeMissle];
        DamageByAttackType magic = info.DamageByAttackTypes[AttackType.Magic];

        Assert.Equal(10, melee.DamageByElements[DamageElement.Slash].TotalNormalDamage);
        Assert.Equal(20, magic.DamageByElements[DamageElement.Fire].TotalNormalDamage);
        Assert.Equal(30, info.TotalNormalDamage);
    }

    private static CombatEventArgs Hit(int damage, bool isCrit) => new(
        "Me", "Drudge", AttackType.MeleeMissle, DamageElement.Slash,
        IsFailedAttack: false, IsCriticalHit: isCrit, IsOverpower: false,
        IsSneakAttack: false, IsRecklessness: false, IsKillingBlow: false, DamageAmount: damage);
}
