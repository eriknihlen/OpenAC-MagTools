namespace OpenAC.MagTools.Trackers.Combat;

/// <summary>
/// Port of the original's <c>Trackers.Combat.CombatInfo</c> — one
/// (SourceName, TargetName) pairing's accumulated combat stats, bucketed by
/// (<see cref="AttackType"/> -&gt; <see cref="DamageElement"/>).
/// </summary>
public sealed class CombatInfo(string sourceName, string targetName)
{
    public string SourceName { get; } = sourceName;

    public string TargetName { get; } = targetName;

    public int KillingBlows { get; set; }

    public Dictionary<AttackType, DamageByAttackType> DamageByAttackTypes { get; } = [];

    public void AddFromCombatEventArgs(CombatEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.IsKillingBlow)
            KillingBlows++;

        if (!DamageByAttackTypes.TryGetValue(args.AttackType, out DamageByAttackType? byAttackType))
        {
            byAttackType = new DamageByAttackType();
            DamageByAttackTypes.Add(args.AttackType, byAttackType);
        }

        if (!byAttackType.DamageByElements.TryGetValue(args.DamageElement, out DamageByElement? byElement))
        {
            byElement = new DamageByElement();
            byAttackType.DamageByElements.Add(args.DamageElement, byElement);
        }

        byElement.TotalAttacks++;

        if (args.IsFailedAttack)
            byElement.FailedAttacks++;

        if (args.IsCriticalHit)
            byElement.Crits++;

        if (!args.IsCriticalHit)
        {
            byElement.TotalNormalDamage += args.DamageAmount;
            if (args.DamageAmount > byElement.MaxNormalDamage)
                byElement.MaxNormalDamage = args.DamageAmount;
        }
        else
        {
            byElement.TotalCritDamage += args.DamageAmount;
            if (args.DamageAmount > byElement.MaxCritDamage)
                byElement.MaxCritDamage = args.DamageAmount;
        }
    }

    public int TotalAttacks => Sum(static a => a.TotalAttacks);

    public int FailedAttacks => Sum(static a => a.FailedAttacks);

    public int TotalNormalDamage => Sum(static a => a.TotalNormalDamage);

    public int MaxNormalDamage => Max(static a => a.MaxNormalDamage);

    public int Crits => Sum(static a => a.Crits);

    public int TotalCritDamage => Sum(static a => a.TotalCritDamage);

    public int MaxCritDamage => Max(static a => a.MaxCritDamage);

    public int Damage => Sum(static a => a.Damage);

    private int Sum(Func<DamageByAttackType, int> selector)
    {
        int value = 0;
        foreach (DamageByAttackType byAttackType in DamageByAttackTypes.Values)
            value += selector(byAttackType);
        return value;
    }

    private int Max(Func<DamageByAttackType, int> selector)
    {
        int value = 0;
        foreach (DamageByAttackType byAttackType in DamageByAttackTypes.Values)
        {
            int candidate = selector(byAttackType);
            if (candidate > value)
                value = candidate;
        }

        return value;
    }
}

/// <summary>Port of the original's nested <c>CombatInfo.DamageByAttackType</c>.</summary>
public sealed class DamageByAttackType
{
    public Dictionary<DamageElement, DamageByElement> DamageByElements { get; } = [];

    public int TotalAttacks => Sum(static e => e.TotalAttacks);

    public int FailedAttacks => Sum(static e => e.FailedAttacks);

    public int TotalNormalDamage => Sum(static e => e.TotalNormalDamage);

    public int MaxNormalDamage => Max(static e => e.MaxNormalDamage);

    public int Crits => Sum(static e => e.Crits);

    public int TotalCritDamage => Sum(static e => e.TotalCritDamage);

    public int MaxCritDamage => Max(static e => e.MaxCritDamage);

    public int Damage => Sum(static e => e.Damage);

    private int Sum(Func<DamageByElement, int> selector)
    {
        int value = 0;
        foreach (DamageByElement byElement in DamageByElements.Values)
            value += selector(byElement);
        return value;
    }

    private int Max(Func<DamageByElement, int> selector)
    {
        int value = 0;
        foreach (DamageByElement byElement in DamageByElements.Values)
        {
            int candidate = selector(byElement);
            if (candidate > value)
                value = candidate;
        }

        return value;
    }
}

/// <summary>
/// Port of the original's nested <c>CombatInfo.DamageByAttackType.DamageByElement</c>
/// — one (AttackType, DamageElement) bucket's raw counters.
/// </summary>
public sealed class DamageByElement
{
    public int TotalAttacks { get; set; }

    public int FailedAttacks { get; set; }

    public int Crits { get; set; }

    public int TotalNormalDamage { get; set; }

    public int MaxNormalDamage { get; set; }

    public int TotalCritDamage { get; set; }

    public int MaxCritDamage { get; set; }

    public int Damage => TotalNormalDamage + TotalCritDamage;
}
