namespace OpenAC.MagTools.Trackers.Combat.Cloaks;

/// <summary>
/// Port of the original's <c>Trackers.Combat.Cloaks.SurgeType</c>.
/// <see cref="RingSpell"/> is unused by the tracker (kept for parity with the
/// original enum shape; nothing ever assigns it).
/// </summary>
public enum SurgeType
{
    Unknown,
    ShroudOfDarknessMelee,
    ShroudOfDarknessMissile,
    ShroudOfDarknessMagic,
    CloakedInSkill,
    DamageReduction,
    RingSpell,
}
