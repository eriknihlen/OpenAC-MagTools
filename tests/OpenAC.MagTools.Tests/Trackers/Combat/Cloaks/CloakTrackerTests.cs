using OpenAC.MagTools.Trackers.Combat.Cloaks;

namespace OpenAC.MagTools.Tests.Trackers.Combat.Cloaks;

public sealed class CloakTrackerTests
{
    private const string Me = "Acdream";

    [Fact]
    public void ShroudOfDarknessMeleeOnAnotherTargetSourcesMe()
    {
        SurgeEventArgs? args = CloakTracker.Parse(
            "You cast Shroud of Darkness (Melee) on Invading Iron Blade Knight", Me);

        Assert.NotNull(args);
        Assert.Equal(SurgeType.ShroudOfDarknessMelee, args!.SurgeType);
        Assert.Equal(Me, args.SourceName);
        Assert.Equal("Invading Iron Blade Knight", args.TargetName);
    }

    [Fact]
    public void ShroudOfDarknessMagicOnAnotherTargetSourcesMe()
    {
        SurgeEventArgs? args = CloakTracker.Parse(
            "You cast Shroud of Darkness (Magic) on Infernal Zefir", Me);

        Assert.NotNull(args);
        Assert.Equal(SurgeType.ShroudOfDarknessMagic, args!.SurgeType);
        Assert.Equal("Infernal Zefir", args.TargetName);
    }

    [Fact]
    public void ShroudOfDarknessOnYourselfHasNoSourceOrTarget()
    {
        SurgeEventArgs? args = CloakTracker.Parse(
            "You cast Shroud of Darkness (Melee) on yourself", Me);

        Assert.NotNull(args);
        Assert.Equal(string.Empty, args!.SourceName);
        Assert.Equal(string.Empty, args.TargetName);
    }

    [Fact]
    public void CloakedInSkillSelfCasts()
    {
        SurgeEventArgs? args = CloakTracker.Parse("You cast Cloaked in Skill on yourself", Me);

        Assert.NotNull(args);
        Assert.Equal(SurgeType.CloakedInSkill, args!.SurgeType);
        Assert.Equal(Me, args.SourceName);
        Assert.Equal(Me, args.TargetName);
    }

    [Fact]
    public void DamageReductionSelfCasts()
    {
        SurgeEventArgs? args = CloakTracker.Parse(
            "Your cloak reduced the damage from 162 down to 0!", Me);

        Assert.NotNull(args);
        Assert.Equal(SurgeType.DamageReduction, args!.SurgeType);
        Assert.Equal(Me, args.SourceName);
        Assert.Equal(Me, args.TargetName);
    }

    [Fact]
    public void ThirdPersonLineIsRecognizedButUncounted()
    {
        SurgeEventArgs? args = CloakTracker.Parse(
            "The cloak of OtherPlayer weaves the magic of Shroud of Darkness (Melee)!", Me);

        // Recognized as a surge type, but with no source (third-person lines
        // never start with "You cast "/"Your cloak "), so CombatTracker's
        // "only count events I sourced" filter drops it — a caller relying
        // on that filter never mis-attributes another player's surge to me.
        Assert.NotNull(args);
        Assert.Equal(SurgeType.ShroudOfDarknessMelee, args!.SurgeType);
        Assert.Equal(string.Empty, args.SourceName);
    }

    [Fact]
    public void UnrelatedTextReturnsNull()
        => Assert.Null(CloakTracker.Parse("You say, \"hello\"", Me));
}
