using OpenAC.MagTools.Trackers.Combat.Aetheria;

namespace OpenAC.MagTools.Tests.Trackers.Combat;

public sealed class AetheriaTrackerTests
{
    [Theory]
    [InlineData("Surge of Destruction", SurgeType.SurgeOfDestruction)]
    [InlineData("Surge of Protection", SurgeType.SurgeOfProtection)]
    [InlineData("Surge of Regeneration", SurgeType.SurgeOfRegeneration)]
    public void SelfOnlySurgesSourceThemselves(string surgeName, SurgeType expected)
    {
        SurgeEventArgs? args = AetheriaTracker.Parse(
            $"Aetheria surges on MyCharactersName with the power of {surgeName}!");

        Assert.NotNull(args);
        Assert.Equal(expected, args!.SurgeType);
        Assert.Equal("MyCharactersName", args.TargetName);
        Assert.Equal("MyCharactersName", args.SourceName);
    }

    [Fact]
    public void AfflictionSurgeHasNoKnownSource()
    {
        SurgeEventArgs? args = AetheriaTracker.Parse(
            "Aetheria surges on Pyreal Target Drudge with the power of Surge of Affliction!");

        Assert.NotNull(args);
        Assert.Equal(SurgeType.SurgeOfAffliction, args!.SurgeType);
        Assert.Equal("Pyreal Target Drudge", args.TargetName);
        Assert.Equal(string.Empty, args.SourceName);
    }

    [Fact]
    public void FesteringSurgeHasNoKnownSource()
    {
        SurgeEventArgs? args = AetheriaTracker.Parse(
            "Aetheria surges on Pyreal Target Drudge with the power of Surge of Festering!");

        Assert.NotNull(args);
        Assert.Equal(SurgeType.SurgeOfFestring, args!.SurgeType);
        Assert.Equal(string.Empty, args.SourceName);
    }

    [Fact]
    public void CastLineIsIgnoredToAvoidDoubleCounting()
        => Assert.Null(AetheriaTracker.Parse("You cast Surge of Destruction on yourself"));

    [Fact]
    public void ExpiredLineIsIgnored()
        => Assert.Null(AetheriaTracker.Parse("Surge of Destruction has expired."));

    [Fact]
    public void UnrelatedTextReturnsNull()
        => Assert.Null(AetheriaTracker.Parse("You say, \"hello\""));
}
