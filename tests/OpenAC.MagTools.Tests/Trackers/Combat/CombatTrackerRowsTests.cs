using OpenAC.MagTools.Trackers.Combat;

namespace OpenAC.MagTools.Tests.Trackers.Combat;

public sealed class CombatTrackerRowsTests
{
    private const string Me = "Acdream";

    [Fact]
    public void MonsterRowsStartWithHeaderThenAllThenOpponentsUnsorted()
    {
        var tracker = new CombatTracker();
        tracker.OnCombatEvent(Hit(Me, "Zebra", 10), Me);
        tracker.OnCombatEvent(Hit(Me, "Apple", 20), Me);

        IReadOnlyList<CombatTrackerRows.MonsterRow> rows =
            CombatTrackerRows.BuildMonsterRows(tracker, Me, sortAlphabetically: false);

        Assert.Equal(4, rows.Count);
        Assert.Null(rows[0].TargetName);
        Assert.Equal(Me, rows[1].TargetName);
        Assert.Equal("Zebra", rows[2].TargetName);
        Assert.Equal("Apple", rows[3].TargetName);
    }

    [Fact]
    public void AlphabeticalSortOrdersOpponentsOrdinally()
    {
        var tracker = new CombatTracker();
        tracker.OnCombatEvent(Hit(Me, "Zebra", 10), Me);
        tracker.OnCombatEvent(Hit(Me, "Apple", 20), Me);
        tracker.OnCombatEvent(Hit(Me, "Mango", 5), Me);

        IReadOnlyList<CombatTrackerRows.MonsterRow> rows =
            CombatTrackerRows.BuildMonsterRows(tracker, Me, sortAlphabetically: true);

        Assert.Equal(["Apple", "Mango", "Zebra"], rows.Skip(2).Select(r => r.TargetName));
    }

    [Fact]
    public void AggregateRowSumsKillingBlowsAndDamageAcrossOpponents()
    {
        var tracker = new CombatTracker();
        tracker.OnCombatEvent(Hit(Me, "Drudge", 10), Me);
        tracker.OnCombatEvent(Kill(Me, "Drudge"), Me);
        tracker.OnCombatEvent(Hit(Me, "Olthoi", 20), Me);
        tracker.OnCombatEvent(Hit("Reaver", Me, 5), Me);

        IReadOnlyList<CombatTrackerRows.MonsterRow> rows =
            CombatTrackerRows.BuildMonsterRows(tracker, Me, sortAlphabetically: false);

        string allRow = rows[1].Text;
        Assert.Contains("1", allRow); // one killing blow
        Assert.Contains("30", allRow); // 10 + 20 damage given
        Assert.Contains("5", allRow); // damage received
    }

    [Fact]
    public void BuildDamageRowsReturnsEmptyForTheHeaderRow()
        => Assert.Empty(CombatTrackerRows.BuildDamageRows(new CombatTracker(), null, Me));

    [Fact]
    public void DamageRowsBreakOutReceivedDamageByElementAndAttackType()
    {
        var tracker = new CombatTracker();
        tracker.OnCombatEvent(
            new CombatEventArgs(
                "Drudge", Me, AttackType.MeleeMissle, DamageElement.Slash,
                false, false, false, false, false, false, 40),
            Me);
        tracker.OnCombatEvent(
            new CombatEventArgs(
                "Drudge", Me, AttackType.Magic, DamageElement.Fire,
                false, false, false, false, false, false, 15),
            Me);

        IReadOnlyList<string> rows = CombatTrackerRows.BuildDamageRows(tracker, "Drudge", Me);

        string slashRow = rows.Single(r => r.TrimStart().StartsWith("Slash", StringComparison.Ordinal));
        Assert.Contains("40", slashRow);

        string totalRow = rows.Single(r => r.TrimStart().StartsWith("Total", StringComparison.Ordinal));
        Assert.Contains("40", totalRow); // Mel/Msl total
        Assert.Contains("15", totalRow); // Magic total
    }

    [Fact]
    public void AttacksPercentIsHitRateExcludingFailedAttacks()
    {
        var tracker = new CombatTracker();
        // 3 attacks against Drudge, 1 of which failed.
        tracker.OnCombatEvent(Hit(Me, "Drudge", 10), Me);
        tracker.OnCombatEvent(Hit(Me, "Drudge", 10), Me);
        tracker.OnCombatEvent(
            new CombatEventArgs(
                Me, "Drudge", AttackType.MeleeMissle, DamageElement.Unknown,
                IsFailedAttack: true, IsCriticalHit: false, IsOverpower: false,
                IsSneakAttack: false, IsRecklessness: false, IsKillingBlow: false, DamageAmount: 0),
            Me);

        IReadOnlyList<string> rows = CombatTrackerRows.BuildDamageRows(tracker, "Drudge", Me);
        string header = rows[0];

        // 2 of 3 hit -> 66.666...% rounds to 67%.
        Assert.Contains("3", header);
        Assert.Contains("67%", header);
    }

    [Fact]
    public void EvadesPercentIsAgainstMeleeDefendsOnly()
    {
        var tracker = new CombatTracker();
        // 4 melee attacks against me, 1 evaded.
        tracker.OnCombatEvent(Hit("Drudge", Me, 10), Me);
        tracker.OnCombatEvent(Hit("Drudge", Me, 10), Me);
        tracker.OnCombatEvent(Hit("Drudge", Me, 10), Me);
        tracker.OnCombatEvent(
            new CombatEventArgs(
                Me, "Drudge", AttackType.MeleeMissle, DamageElement.Unknown,
                IsFailedAttack: true, IsCriticalHit: false, IsOverpower: false,
                IsSneakAttack: false, IsRecklessness: false, IsKillingBlow: false, DamageAmount: 0),
            Me);

        // The evaded flag belongs to the DEFENDER direction: "Drudge evaded
        // your attack" would be source=me — for an EVADE OF an incoming
        // attack the source is the monster and IsFailedAttack is on that
        // same (monster -> me) pairing.
        tracker.OnCombatEvent(
            new CombatEventArgs(
                "Drudge", Me, AttackType.MeleeMissle, DamageElement.Unknown,
                IsFailedAttack: true, IsCriticalHit: false, IsOverpower: false,
                IsSneakAttack: false, IsRecklessness: false, IsKillingBlow: false, DamageAmount: 0),
            Me);

        IReadOnlyList<string> rows = CombatTrackerRows.BuildDamageRows(tracker, "Drudge", Me);
        string typelessRow = rows.Single(r => r.TrimStart().StartsWith("Typeless", StringComparison.Ordinal));

        // 4 melee defends (3 hits + 1 evade), 1 evaded -> 25%.
        Assert.Contains("4", typelessRow);
        Assert.Contains("25%", typelessRow);
    }

    private static CombatEventArgs Hit(string source, string target, int damage) => new(
        source, target, AttackType.MeleeMissle, DamageElement.Slash,
        IsFailedAttack: false, IsCriticalHit: false, IsOverpower: false,
        IsSneakAttack: false, IsRecklessness: false, IsKillingBlow: false, DamageAmount: damage);

    private static CombatEventArgs Kill(string source, string target) => new(
        source, target, AttackType.Unknown, DamageElement.Unknown,
        IsFailedAttack: false, IsCriticalHit: false, IsOverpower: false,
        IsSneakAttack: false, IsRecklessness: false, IsKillingBlow: true, DamageAmount: 0);
}
