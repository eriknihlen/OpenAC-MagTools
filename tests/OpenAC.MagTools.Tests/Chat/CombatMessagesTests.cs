using OpenAC.MagTools.Chat;

namespace OpenAC.MagTools.Tests.Chat;

public sealed class CombatMessagesTests
{
    [Theory]
    [InlineData("You flatten Noble Remains's body with the force of your assault!")]
    [InlineData("You bring Wight Blade Sorcerer to a fiery end!")]
    [InlineData("You obliterate Drudge Skulker!")]
    [InlineData("You killed SomeGuy!")]
    [InlineData("Wight is reduced to cinders!")]
    [InlineData("Electricity tears Insatiable Eater apart!")]
    [InlineData("Blistered by lightning, Insatiable Eater falls!")]
    [InlineData("Insatiable Eater is incinerated by your assault!")]
    public void IsKilledByMeMessageMatchesEveryPortedShape(string text)
        => Assert.True(CombatMessages.IsKilledByMeMessage(text));

    [Theory]
    [InlineData("You evaded Remoran Corsair!")]
    [InlineData("Ruschk Sadist evaded your attack.")]
    [InlineData("You say, \"hello\"")]
    [InlineData("Critical hit!  You scorch Annihilator for 685 points of fire damage!")]
    public void IsKilledByMeMessageRejectsUnrelatedLines(string text)
        => Assert.False(CombatMessages.IsKilledByMeMessage(text));

    [Theory]
    [InlineData("You evaded Remoran Corsair!")]
    [InlineData("Ruschk Sadist evaded your attack.")]
    [InlineData("You resist the spell cast by Remoran Corsair")]
    [InlineData("Sentient Crystal Shard resists your spell")]
    public void IsFailedAttackMatchesEveryPortedShape(string text)
        => Assert.True(CombatMessages.IsFailedAttack(text));

    [Fact]
    public void IsFailedAttackRejectsAnOrdinaryDamageLine()
        => Assert.False(CombatMessages.IsFailedAttack("You scorch Annihilator for 805 points of fire damage!"));

    [Fact]
    public void GetKilledByMeTargetExtractsTheName()
        => Assert.Equal("Drudge Skulker", CombatMessages.GetKilledByMeTarget("You obliterate Drudge Skulker!"));

    [Fact]
    public void GetKilledByMeTargetReturnsNullForUnrelatedText()
        => Assert.Null(CombatMessages.GetKilledByMeTarget("You say, \"hello\""));

    /// <summary>
    /// Appendix B item 5 of the port design's research inventory: the
    /// upstream <c>MeleeMissileGivenAttacks[1]</c> (the plain
    /// <c>^You [\w]+ ... $</c> pattern) was ordered before the Sneak-Attack/
    /// Recklessness variants. This asserts the FIX directly against the
    /// table itself — the specific patterns appear at a lower index than the
    /// general one — so a future edit that reintroduces the upstream order
    /// fails here even if <see cref="Trackers.Combat.StandardTracker"/>'s own
    /// tests would still pass (since, per the docs/deviations.md analysis,
    /// .NET's anchoring makes the two orderings behaviorally identical for
    /// every known retail line shape — the table order itself is what this
    /// guards, not a parse-result regression).
    /// </summary>
    [Fact]
    public void MeleeMissileGivenAttacksOrdersSpecificPatternsBeforeTheGeneralOne()
    {
        int generalIndex = -1;
        int sneakIndex = -1;
        int recklessIndex = -1;
        int sneakRecklessIndex = -1;

        for (int i = 0; i < CombatMessages.MeleeMissileGivenAttacks.Length; i++)
        {
            string pattern = CombatMessages.MeleeMissileGivenAttacks[i].ToString();
            if (pattern.StartsWith("^You ", StringComparison.Ordinal))
                generalIndex = i;
            else if (pattern.StartsWith("^Sneak Attack! Recklessness!", StringComparison.Ordinal))
                sneakRecklessIndex = i;
            else if (pattern.StartsWith("^Sneak Attack!", StringComparison.Ordinal))
                sneakIndex = i;
            else if (pattern.StartsWith("^Recklessness!", StringComparison.Ordinal))
                recklessIndex = i;
        }

        Assert.True(generalIndex >= 0);
        Assert.True(sneakIndex >= 0 && sneakIndex < generalIndex);
        Assert.True(recklessIndex >= 0 && recklessIndex < generalIndex);
        Assert.True(sneakRecklessIndex >= 0 && sneakRecklessIndex < generalIndex);
    }
}
