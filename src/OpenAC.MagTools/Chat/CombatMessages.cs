using System.Text.RegularExpressions;

namespace OpenAC.MagTools.Chat;

/// <summary>
/// Port of the original's <c>Trackers.Combat.Standard.CombatMessages</c> —
/// the full set of combat-line regex tables. <see cref="IsKilledByMeMessage"/>
/// and the 36 "you killed it" regexes were pulled forward for the chat
/// filter's Monster Deaths rule; the P4 combat tracker slice
/// (<c>Trackers.Combat.StandardTracker</c>) adds the rest: failed-attack
/// (evade/resist) lines, and melee/missile/magic damage-dealt and
/// damage-received lines.
/// </summary>
/// <remarks>
/// <see cref="MeleeMissileGivenAttacks"/> orders the Sneak-Attack/Recklessness
/// variants BEFORE the plain "You [verb] X for N points..." pattern —
/// deliberately different from the original's literal order, which listed the
/// general pattern second (right after the plain-crit pattern). See
/// docs/deviations.md for why: under .NET's default (non-Multiline) anchoring
/// the general pattern's leading <c>^You</c> cannot actually match a line that
/// starts with the literal text "Sneak Attack!" or "Recklessness!", so this
/// reordering does not change which lines match — it only removes the
/// visual/maintenance hazard the upstream ordering has (Appendix B item 5 of
/// the port's research inventory).
/// </remarks>
public static class CombatMessages
{
    // ── Failed attacks (evade / resist) — direction inferred by the caller ──

    /// <summary>
    /// You evaded X! / X evaded your attack. / You resist the spell cast by
    /// X / X resists your spell.
    /// </summary>
    public static readonly Regex[] FailedAttacks =
    [
        new(@"^You evaded (?<targetname>.+)!$"),
        new(@"^(?<targetname>.+) evaded your attack\.$"),
        new(@"^You resist the spell cast by (?<targetname>.+)$"),
        new(@"^(?<targetname>.+) resists your spell$"),
    ];

    /// <summary>Melee/missile damage a monster dealt to the local player (received).</summary>
    public static readonly Regex[] MeleeMissileReceivedAttacks =
    [
        new(@"^Critical hit! Overpower! (?<targetname>.+) [\w]+ your .+ for (?<points>.*) point.* of .+ damage.*$"),
        new(@"^Critical hit! (?<targetname>.+) [\w]+ your .+ for (?<points>.*) point.* of .+ damage.*$"),
        new(@"^Overpower! (?<targetname>.+) [\w]+ your .+ for (?<points>.+) point.* of .+ damage.*$"),
        new(@"^(?<targetname>.+) [\w]+ your .+ for (?<points>.+) point.* of .+ damage.*$"),
    ];

    /// <summary>
    /// Melee/missile damage the local player dealt (given). See the class
    /// remarks for why the Sneak-Attack/Recklessness variants come before the
    /// plain pattern here.
    /// </summary>
    public static readonly Regex[] MeleeMissileGivenAttacks =
    [
        new(@"^Critical hit!  Sneak Attack! You [\w]+ (?<targetname>.*) for (?<points>.+) point.* of .+ damage.*$"),
        new(@"^Sneak Attack! Recklessness! You [\w]+ (?<targetname>.*) for (?<points>.+) point.* of .+ damage.*$"),
        new(@"^Sneak Attack! You [\w]+ (?<targetname>.*) for (?<points>.+) point.* of .+ damage.*$"),
        new(@"^Recklessness! You [\w]+ (?<targetname>.*) for (?<points>.+) point.* of .+ damage.*$"),
        // Note the TWO spaces after "Critical hit!" here — retail's actual
        // spacing on the "given" side, asymmetric with the one space on the
        // "received" side above.
        new(@"^Critical hit!  You [\w]+ (?<targetname>.*) for (?<points>.+) point.* of .+ damage.*$"),
        new(@"^You [\w]+ (?<targetname>.*) for (?<points>.+) point.* of .+ damage.*$"),
    ];

    /// <summary>Magic damage a monster dealt to the local player (received).</summary>
    public static readonly Regex[] MagicReceivedAttacks =
    [
        new(@"^Critical hit! Overpower! (?<targetname>.+) [\w]+ you for (?<points>.+) point.* with .+$"),
        new(@"^Critical hit! (?<targetname>.+) [\w]+ you for (?<points>.+) point.* with .+$"),
        new(@"^Overpower! (?<targetname>.+) [\w]+ you for (?<points>.+) point.* with .+$"),
        new(@"^(?<targetname>.+) [\w]+ you for (?<points>.+) point.* with .+$"),
        new(@"^Magical energies lose (?<points>.+) point.* of health due to (?<targetname>.+) casting .+$"),
        new(@"^You lose (?<points>.+) point.* of health due to (?<targetname>.+) casting .+$"),
        new(@"^(?<targetname>.+) casts .+ and drains (?<points>.+) point.* .+$"),
    ];

    /// <summary>Magic damage the local player dealt (given).</summary>
    public static readonly Regex[] MagicGivenAttacks =
    [
        new(@"^Critical hit! You [\w]+ (?<targetname>.+) for (?<points>.+) point.* with .+$"),
        new(@"^You [\w]+ (?<targetname>.+) for (?<points>.+) point.* with .+$"),
    ];

    /// <summary>
    /// Attributes a target with zero damage (a buff-cast line, not an
    /// attack) — used only to seed a <see cref="Combat.CombatInfo"/> pairing.
    /// </summary>
    public static readonly Regex[] MagicCastAttacks =
    [
        new(@"^You cast Gossamer Flesh on (?<targetname>((?!, ).)+)$"),
        new(@"^You cast Gossamer Flesh on (?<targetname>.+), .+$"),
    ];

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

    /// <summary>
    /// True if <paramref name="text"/> matches one of <see cref="FailedAttacks"/>.
    /// </summary>
    public static bool IsFailedAttack(string text)
    {
        foreach (Regex regex in FailedAttacks)
        {
            if (regex.IsMatch(text))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Matches <paramref name="text"/> against every regex in
    /// <paramref name="table"/> in order and returns the first successful
    /// match, or null.
    /// </summary>
    public static Match? FirstMatch(IReadOnlyList<Regex> table, string text)
    {
        foreach (Regex regex in table)
        {
            Match match = regex.Match(text);
            if (match.Success)
                return match;
        }

        return null;
    }

    /// <summary>
    /// The killed-by-me target name, for the same "You killed it" table
    /// <see cref="IsKilledByMeMessage"/> tests against.
    /// </summary>
    public static string? GetKilledByMeTarget(string text)
    {
        Match? match = FirstMatch(TargetKilledByMe, text);
        return match?.Groups["targetname"].Value;
    }
}
