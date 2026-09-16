namespace OpenAC.MagTools.Trackers.Combat.Cloaks;

/// <summary>
/// Port of the original's <c>Trackers.Combat.Cloaks.CloakTracker</c> — a pure
/// line parser (see the remarks on
/// <see cref="Trackers.Combat.StandardTracker"/> for why).
/// </summary>
public static class CloakTracker
{
    public static SurgeEventArgs? Parse(string text, string localPlayerName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(localPlayerName);

        string sourceName = string.Empty;
        string targetName = string.Empty;

        if (text.StartsWith("You cast ", StringComparison.Ordinal)
            || text.StartsWith("Your cloak ", StringComparison.Ordinal))
        {
            if (text.Contains("Shroud of Darkness", StringComparison.Ordinal)
                && !text.Contains("yourself", StringComparison.Ordinal))
            {
                sourceName = localPlayerName;

                int onIndex = text.IndexOf(" on ", StringComparison.Ordinal);
                if (onIndex >= 0)
                    targetName = text[(onIndex + 4)..];
            }

            if (text.Contains("Cloaked in Skill", StringComparison.Ordinal))
            {
                sourceName = localPlayerName;
                targetName = localPlayerName;
            }

            if (text.Contains("Your cloak reduced the damage", StringComparison.Ordinal))
            {
                sourceName = localPlayerName;
                targetName = localPlayerName;
            }
        }

        SurgeType surgeType = SurgeType.Unknown;

        if (text.Contains("Shroud of Darkness (Melee)", StringComparison.Ordinal))
            surgeType = SurgeType.ShroudOfDarknessMelee;
        if (text.Contains("Shroud of Darkness (Missile)", StringComparison.Ordinal))
            surgeType = SurgeType.ShroudOfDarknessMissile;
        if (text.Contains("Shroud of Darkness (Magic)", StringComparison.Ordinal))
            surgeType = SurgeType.ShroudOfDarknessMagic;
        if (text.Contains("Cloaked in Skill", StringComparison.Ordinal))
            surgeType = SurgeType.CloakedInSkill;
        if (text.Contains("Your cloak reduced the damage", StringComparison.Ordinal))
            surgeType = SurgeType.DamageReduction;

        if (surgeType == SurgeType.Unknown)
            return null;

        return new SurgeEventArgs(sourceName, targetName, surgeType);
    }
}
