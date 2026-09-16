namespace OpenAC.MagTools.Trackers.Combat.Aetheria;

/// <summary>
/// Port of the original's <c>Trackers.Combat.Aetheria.AetheriaTracker</c> —
/// a pure line parser (see the remarks on
/// <see cref="Trackers.Combat.StandardTracker"/> for why this is no longer a
/// chat-event subscriber in its own right).
/// </summary>
public static class AetheriaTracker
{
    private const string SurgePrefix = "Aetheria surges on ";
    private const string SurgeInfix = " with the ";

    public static SurgeEventArgs? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Prevent duplicate event raises for the same aetheria surge: retail
        // prints both a cast line and the surge line for a self-only surge.
        if (text.StartsWith("You cast", StringComparison.Ordinal)
            || text.Contains("has expired.", StringComparison.Ordinal))
            return null;

        string sourceName = string.Empty;
        string targetName = string.Empty;

        if (text.StartsWith(SurgePrefix, StringComparison.Ordinal)
            && text.Contains(SurgeInfix, StringComparison.Ordinal))
        {
            string afterPrefix = text[SurgePrefix.Length..];
            int infixIndex = afterPrefix.IndexOf(SurgeInfix, StringComparison.Ordinal);
            targetName = afterPrefix[..infixIndex];

            // These surges can only be cast on yourself.
            if (text.Contains("Surge of Destruction", StringComparison.Ordinal)
                || text.Contains("Surge of Protection", StringComparison.Ordinal)
                || text.Contains("Surge of Regeneration", StringComparison.Ordinal))
            {
                sourceName = targetName;
            }
        }

        SurgeType surgeType = SurgeType.Unknown;
        if (text.Contains("Surge of Destruction", StringComparison.Ordinal))
            surgeType = SurgeType.SurgeOfDestruction;
        if (text.Contains("Surge of Protection", StringComparison.Ordinal))
            surgeType = SurgeType.SurgeOfProtection;
        if (text.Contains("Surge of Regeneration", StringComparison.Ordinal))
            surgeType = SurgeType.SurgeOfRegeneration;
        if (text.Contains("Surge of Affliction", StringComparison.Ordinal))
            surgeType = SurgeType.SurgeOfAffliction;
        if (text.Contains("Surge of Festering", StringComparison.Ordinal))
            surgeType = SurgeType.SurgeOfFestring;

        if (surgeType == SurgeType.Unknown)
            return null;

        return new SurgeEventArgs(sourceName, targetName, surgeType);
    }
}
