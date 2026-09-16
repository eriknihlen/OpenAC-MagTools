using System.Text.RegularExpressions;
using OpenAC.MagTools.Chat;

namespace OpenAC.MagTools.Trackers.Combat;

/// <summary>
/// Port of the original's <c>Trackers.Combat.Standard.StandardTracker</c> —
/// the damage/evade/kill parse pipeline. A pure function rather than a chat
/// subscriber: the original constructed one instance per <c>CombatTracker</c>
/// (two: current session + persistent) and each independently subscribed to
/// the chat event, parsing every combat line twice. This port parses once —
/// <see cref="Trackers.Combat.CombatTracker"/>'s single feed calls
/// <see cref="Parse"/> and forwards the same <see cref="CombatEventArgs"/> to
/// both the current-session and persistent instances. See docs/deviations.md.
/// </summary>
public static class StandardTracker
{
    /// <summary>
    /// Parses one combat-classified chat line. Returns null for a line that
    /// carries no source/target (nothing to record) — same as the original's
    /// early return when both names are empty. <paramref name="diagnostic"/>
    /// is invoked with the original's exact diagnostic text when the line
    /// looked like an attack but its type or damage element could not be
    /// resolved.
    /// </summary>
    public static CombatEventArgs? Parse(
        string text, string localPlayerName, Action<string>? diagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(localPlayerName);

        if (text.Length == 0)
            return null;

        string sourceName = string.Empty;
        string targetName = string.Empty;

        AttackType attackType = AttackType.Unknown;
        DamageElement damageElement = DamageElement.Unknown;

        bool isFailedAttack = false;
        bool isCriticalHit = text.Contains("Critical hit!", StringComparison.Ordinal);
        bool isOverpower = text.Contains("Overpower!", StringComparison.Ordinal);
        bool isSneakAttack = text.Contains("Sneak Attack!", StringComparison.Ordinal);
        // The host's DefenderLine (an incoming reckless attack) emits
        // "Reckless! ", not "Recklessness! " — only AttackerLine (the local
        // player's own outgoing attack) uses the longer word. Accept both
        // spellings; see docs/deviations.md.
        bool isRecklessness = text.Contains("Recklessness!", StringComparison.Ordinal)
            || text.Contains("Reckless!", StringComparison.Ordinal);
        bool isKillingBlow = false;

        int damageAmount = 0;

        if (CombatMessages.IsFailedAttack(text))
        {
            isFailedAttack = true;

            string parsedName = string.Empty;
            Match? match = CombatMessages.FirstMatch(CombatMessages.FailedAttacks, text);
            if (match is not null)
                parsedName = match.Groups["targetname"].Value;

            if (text.StartsWith("You evaded ", StringComparison.Ordinal))
            {
                sourceName = parsedName;
                targetName = localPlayerName;
                attackType = AttackType.MeleeMissle;
            }
            else if (text.Contains(" evaded your attack", StringComparison.Ordinal))
            {
                sourceName = localPlayerName;
                targetName = parsedName;
                attackType = AttackType.MeleeMissle;
            }
            else if (text.StartsWith("You resist the spell cast by ", StringComparison.Ordinal))
            {
                sourceName = parsedName;
                targetName = localPlayerName;
                attackType = AttackType.Magic;
            }
            else if (text.Contains(" resists your spell", StringComparison.Ordinal))
            {
                sourceName = localPlayerName;
                targetName = parsedName;
                attackType = AttackType.Magic;
            }
        }
        else if (CombatMessages.IsKilledByMeMessage(text))
        {
            isKillingBlow = true;
            sourceName = localPlayerName;
            targetName = CombatMessages.GetKilledByMeTarget(text) ?? string.Empty;
        }
        else
        {
            // A hostile monster's AI can use Dirty Fighting against the
            // local player too, so a RECEIVED damage line can carry the same
            // "Sneak Attack! "/"Reckless! " prefix the given-side tables
            // already special-case. Strip it before matching so the
            // received tables' greedy leading targetname group never
            // swallows the prefix as part of the attacker's name — see
            // CombatMessages.StripReceivedPrefixes and docs/deviations.md.
            string receivedText = CombatMessages.StripReceivedPrefixes(text);

            Match? match = CombatMessages.FirstMatch(CombatMessages.MeleeMissileReceivedAttacks, receivedText);
            if (match is not null)
            {
                sourceName = match.Groups["targetname"].Value;
                targetName = localPlayerName;
                attackType = AttackType.MeleeMissle;
                damageElement = GetElementFromText(text);
                int.TryParse(match.Groups["points"].Value, out damageAmount);
                goto Found;
            }

            match = CombatMessages.FirstMatch(CombatMessages.MeleeMissileGivenAttacks, text);
            if (match is not null)
            {
                sourceName = localPlayerName;
                targetName = match.Groups["targetname"].Value;
                attackType = AttackType.MeleeMissle;
                damageElement = GetElementFromText(text);
                int.TryParse(match.Groups["points"].Value, out damageAmount);
                goto Found;
            }

            match = CombatMessages.FirstMatch(CombatMessages.MagicReceivedAttacks, receivedText);
            if (match is not null)
            {
                sourceName = match.Groups["targetname"].Value;
                targetName = localPlayerName;
                attackType = AttackType.Magic;
                damageElement = GetElementFromText(text);
                int.TryParse(match.Groups["points"].Value, out damageAmount);
                goto Found;
            }

            match = CombatMessages.FirstMatch(CombatMessages.MagicGivenAttacks, text);
            if (match is not null)
            {
                sourceName = localPlayerName;
                targetName = match.Groups["targetname"].Value;
                attackType = AttackType.Magic;
                damageElement = GetElementFromText(text);
                int.TryParse(match.Groups["points"].Value, out damageAmount);
                goto Found;
            }

            match = CombatMessages.FirstMatch(CombatMessages.MagicCastAttacks, text);
            if (match is not null)
            {
                sourceName = localPlayerName;
                targetName = match.Groups["targetname"].Value;
                attackType = AttackType.Magic;
                damageElement = DamageElement.None;
            }

            Found: ;
        }

        if (sourceName.Length == 0 && targetName.Length == 0)
            return null;

        if (!isKillingBlow && attackType == AttackType.Unknown)
            diagnostic?.Invoke("Unable to parse attack type from: " + text);

        if (!isKillingBlow && !isFailedAttack && damageElement == DamageElement.Unknown)
            diagnostic?.Invoke("Unable to parse damage element from: " + text);

        return new CombatEventArgs(
            sourceName,
            targetName,
            attackType,
            damageElement,
            isFailedAttack,
            isCriticalHit,
            isOverpower,
            isSneakAttack,
            isRecklessness,
            isKillingBlow,
            damageAmount);
    }

    /// <summary>
    /// Port of the original's <c>GetElementFromText</c> — first match wins,
    /// checked in this exact order.
    /// </summary>
    internal static DamageElement GetElementFromText(string text)
    {
        if (Contains(text, "slash") || Contains(text, " cut") || Contains(text, " scratch")
            || Contains(text, " mangle"))
            return DamageElement.Slash;

        if (Contains(text, "pierc") || Contains(text, " gore") || Contains(text, " impale")
            || Contains(text, " nick") || Contains(text, " stab"))
            return DamageElement.Pierce;

        if (Contains(text, "bludge") || Contains(text, " smash") || Contains(text, " bash")
            || Contains(text, " graze") || Contains(text, " crush"))
            return DamageElement.Bludge;

        if (Contains(text, "fire") || Contains(text, " burn") || Contains(text, " singe")
            || Contains(text, " scorch") || Contains(text, " incinerate"))
            return DamageElement.Fire;

        if (Contains(text, "cold") || Contains(text, " frost") || Contains(text, " chill")
            || Contains(text, " numb") || Contains(text, " freeze"))
            return DamageElement.Cold;

        if (Contains(text, "acid") || Contains(text, " blister") || Contains(text, " sear")
            || Contains(text, " corrode") || Contains(text, " dissolve"))
            return DamageElement.Acid;

        if (Contains(text, "electric") || Contains(text, " lightning") || Contains(text, " jolt")
            || Contains(text, " shock") || Contains(text, " spark") || Contains(text, " blast"))
            return DamageElement.Electric;

        // Nether
        if (Contains(text, " eradicate") || Contains(text, " wither") || Contains(text, " scar")
            || Contains(text, " twist"))
            return DamageElement.Typeless;

        // Typeless/Life
        if (Contains(text, " deplete") || Contains(text, " siphon") || Contains(text, " exhaust")
            || Contains(text, " drain") || Contains(text, " lose ") || Contains(text, " health "))
            return DamageElement.Typeless;

        return DamageElement.Unknown;
    }

    private static bool Contains(string text, string value)
        => text.Contains(value, StringComparison.Ordinal);
}
