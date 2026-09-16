namespace OpenAC.MagTools.Trackers.Combat;

/// <summary>
/// Port of the original's <c>Trackers.Combat.Standard.CombatEventArgs</c> —
/// one parsed combat chat line.
/// </summary>
public sealed record CombatEventArgs(
    string SourceName,
    string TargetName,
    AttackType AttackType,
    DamageElement DamageElement,
    bool IsFailedAttack,
    bool IsCriticalHit,
    bool IsOverpower,
    bool IsSneakAttack,
    bool IsRecklessness,
    bool IsKillingBlow,
    int DamageAmount);
