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
}
