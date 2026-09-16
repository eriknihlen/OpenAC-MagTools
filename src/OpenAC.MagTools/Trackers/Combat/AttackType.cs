namespace OpenAC.MagTools.Trackers.Combat;

/// <summary>
/// Port of the original's <c>Trackers.Combat.Standard.AttackType</c>. Spelled
/// <c>MeleeMissle</c> verbatim (the original's typo) because it is used as an
/// XML element name in the combat tracker's export/import format — renaming
/// it would break round-tripping a file exported by an older build.
/// </summary>
public enum AttackType
{
    Unknown,
    MeleeMissle,
    Magic,
}
