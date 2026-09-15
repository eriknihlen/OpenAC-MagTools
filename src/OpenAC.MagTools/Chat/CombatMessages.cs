using System.Text.RegularExpressions;

namespace OpenAC.MagTools.Chat;

/// <summary>
/// Port of the original's <c>Trackers.Combat.Standard.CombatMessages</c> —
/// only <see cref="IsKilledByMeMessage"/> and the 36 "you killed it" regexes
/// behind it. The rest of that class (failed-attack and damage-line parsing)
/// belongs to the combat tracker slice; this table is pulled forward now
/// because the chat filter's Monster Deaths rule needs it too.
/// </summary>
public static class CombatMessages
{
    private static readonly Regex[] TargetKilledByMe =
    [
        // You flatten Noble Remains's body with the force of your assault!
        new(@"^You flatten (?<targetname>.+)'s body with the force of your assault!$"),
        // You bring Wight Blade Sorcerer to a fiery end! (Fire)
        new(@"^You bring (?<targetname>.+) to a fiery end!$"),
        // You beat Door to a lifeless pulp!
        new(@"^You beat (?<targetname>.+) to a lifeless pulp!$"),
        // You smite Famished Eater mightily!
        new(@"^You smite (?<targetname>.+) mightily!$"),
        // You obliterate Drudge Skulker!
        new(@"^You obliterate (?<targetname>.+)!$"),
        // You run Insatiable Eater through! (Pierce)
        new(@"^You run (?<targetname>.+) through!$"),
        // You reduce Insatiable Eater to a sizzling, oozing mass! (Acid)
        new(@"^You reduce (?<targetname>.+) to a sizzling, oozing mass!$"),
        // You knock Blockade Guard into next Morningthaw!
        new(@"^You knock (?<targetname>.+) into next Morningthaw!$"),
        // You split Blockade Guard apart! (Slash)
        new(@"^You split (?<targetname>.+) apart!$"),
        // You cleave Blockade Guard in twain! (Slash)
        new(@"^You cleave (?<targetname>.+) in twain!$"),
        // You slay Blockade Guard viciously enough to impart death several times over! (Slash)
        new(@"^You slay (?<targetname>.+) viciously enough to impart death several times over!$"),
        // You reduce ____ to a drained, twisted corpse! (Nether)
        new(@"^You reduce (?<targetname>.+) to a drained, twisted corpse!$"),
        // Your killing blow nearly turns Shivering Crystalline Wisp inside-out!
        new(@"^Your killing blow nearly turns (?<targetname>.+) inside-out!$"),
        // Your attack stops Ruschk Draktehn cold! (Frost)
        new(@"^Your attack stops (?<targetname>.+) cold!$"),
        // Your lightning coruscates over Insatiable Eater's mortal remains! (Lightning)
        new(@"^Your lightning coruscates over (?<targetname>.+)'s mortal remains!$"),
        // Your assault sends Ardent Moar to an icy death!
        new(@"^Your assault sends (?<targetname>.+) to an icy death!$"),
        // You killed SomeGuy!
        new(@"^You killed (?<targetname>.+)!$"),
        // The thunder of crushing Pyre Minion is followed by the deafening silence of death!
        new(@"^The thunder of crushing (?<targetname>.+) is followed by the deafening silence of death!$"),
        // The deadly force of your attack is so strong that Young Banderling's ancestors feel it!
        new(@"^The deadly force of your attack is so strong that (?<targetname>.+)'s ancestors feel it!$"),
        // Wight Blade Sorcerer's seared corpse smolders before you! (Fire)
        new(@"^(?<targetname>.+)'s seared corpse smolders before you!$"),
        // Wight is reduced to cinders! (Fire)
        new(@"^(?<targetname>.+) is reduced to cinders!$"),
        // Old Bones is shattered by your assault!
        new(@"^(?<targetname>.+) is shattered by your assault!$"),
        // Gnawer Shreth catches your attack, with dire consequences!
        new(@"^(?<targetname>.+) catches your attack, with dire consequences!$"),
        // Gnawer Shreth is utterly destroyed by your attack!
        new(@"^(?<targetname>.+) is utterly destroyed by your attack!$"),
        // Ruschk Laktar suffers a frozen fate! (Frost)
        new(@"^(?<targetname>.+) suffers a frozen fate!$"),
        // Insatiable Eater's perforated corpse falls before you! (Pierce)
        new(@"^(?<targetname>.+)'s perforated corpse falls before you!$"),
        // Insatiable Eater is fatally punctured! (Pierce)
        new(@"^(?<targetname>.+) is fatally punctured!$"),
        // Insatiable Eater's death is preceded by a sharp, stabbing pain! (Pierce)
        new(@"^(?<targetname>.+)'s death is preceded by a sharp, stabbing pain!$"),
        // Insatiable Eater is torn to ribbons by your assault! (Slash)
        new(@"^(?<targetname>.+) is torn to ribbons by your assault!$"),
        // Insatiable Eater is liquified by your attack! (Acid)
        new(@"^(?<targetname>.+) is liquified by your attack!$"),
        // Insatiable Eater's last strength dissolves before you! (Acid)
        new(@"^(?<targetname>.+)'s last strength dissolves before you!$"),
        // Electricity tears Insatiable Eater apart! (Lightning)
        new(@"^Electricity tears (?<targetname>.+) apart!$"),
        // Blistered by lightning, Insatiable Eater falls! (Lightning)
        new(@"^Blistered by lightning, (?<targetname>.+) falls!$"),
        // ____'s last strength withers before you! (Nether)
        new(@"^(?<targetname>.+)'s last strength withers before you!$"),
        // ____ is dessicated by your attack! (Nether)
        new(@"^(?<targetname>.+) is dessicated by your attack!$"),
        // ____ is incinerated by your assault! (Fire)
        new(@"^(?<targetname>.+) is incinerated by your assault!$"),
    ];

    public static bool IsKilledByMeMessage(string text)
    {
        foreach (Regex regex in TargetKilledByMe)
        {
            if (regex.IsMatch(text))
                return true;
        }

        return false;
    }
}
