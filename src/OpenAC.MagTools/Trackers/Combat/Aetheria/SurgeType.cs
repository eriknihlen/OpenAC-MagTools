namespace OpenAC.MagTools.Trackers.Combat.Aetheria;

/// <summary>
/// Port of the original's <c>Trackers.Combat.Aetheria.SurgeType</c>. The
/// last value is spelled <c>SurgeOfFestring</c> verbatim — the original's
/// typo for "Festering" — kept because it is an XML-round-trip-visible name
/// via <c>Enum.ToString()</c>/<c>Enum.Parse</c> would be if this type were
/// ever serialized (it is not, today — <see cref="AetheriaInfo"/> only
/// serializes the surge count), but keeping enum member spelling literal
/// avoids a needless divergence from the reference source.
/// </summary>
public enum SurgeType
{
    Unknown,
    SurgeOfDestruction,
    SurgeOfProtection,
    SurgeOfRegeneration,
    SurgeOfAffliction,
    SurgeOfFestring,
}
