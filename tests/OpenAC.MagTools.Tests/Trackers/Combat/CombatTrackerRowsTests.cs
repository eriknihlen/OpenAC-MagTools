using OpenAC.MagTools.Trackers.Combat;
using AetheriaNs = OpenAC.MagTools.Trackers.Combat.Aetheria;
using CloaksNs = OpenAC.MagTools.Trackers.Combat.Cloaks;

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
        Assert.Equal("", rows[0].Name);
        Assert.Equal("KB's", rows[0].KillingBlows);
        Assert.Equal("Dmg Rcvd", rows[0].DamageReceived);
        Assert.Equal("Dmg Givn", rows[0].DamageGiven);
        Assert.Equal(Me, rows[1].TargetName);
        Assert.Equal("All", rows[1].Name);
        Assert.Equal("Zebra", rows[2].TargetName);
        Assert.Equal("Zebra", rows[2].Name);
        Assert.Equal("Apple", rows[3].TargetName);
        Assert.Equal("Apple", rows[3].Name);
    }

    [Fact]
    public void MonsterRowsAreAlsoSeededFromAetheriaAndCloakSurgesWithNoPlainDamage()
    {
        // The original's CombatTrackerGUI seeded the monster list from all
        // three trackers. An opponent the local player only ever
        // aetheria/cloak-surged against — no melee/missile/magic hit
        // recorded at all — must still get a monster-list row.
        var tracker = new CombatTracker();
        tracker.OnAetheriaSurge(
            new AetheriaNs.SurgeEventArgs(
                Me, "Aetheria Only Target", AetheriaNs.SurgeType.SurgeOfAffliction),
            Me);
        tracker.OnCloakSurge(
            new CloaksNs.SurgeEventArgs(
                Me, "Cloak Only Target", CloaksNs.SurgeType.ShroudOfDarknessMelee),
            Me);

        IReadOnlyList<CombatTrackerRows.MonsterRow> rows =
            CombatTrackerRows.BuildMonsterRows(tracker, Me, sortAlphabetically: true);

        Assert.Contains(rows, r => r.TargetName == "Aetheria Only Target");
        Assert.Contains(rows, r => r.TargetName == "Cloak Only Target");
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

        // Exact padded values (right-aligned into the column's pad width via
        // PadRightAligned), not a bare Contains — "Contains("1")" would also
        // match "10", "15", "21", etc. and prove nothing.
        CombatTrackerRows.MonsterRow allRow = rows[1];
        Assert.Equal("     1", allRow.KillingBlows); // one killing blow, padded to 6
        Assert.Equal("        30", allRow.DamageGiven); // 10 + 20 damage given, padded to 10
        Assert.Equal("         5", allRow.DamageReceived); // damage received, padded to 10
    }

    [Fact]
    public void MonsterRowNumericColumnsAreRightAlignedByPadding()
    {
        var tracker = new CombatTracker();
        tracker.OnCombatEvent(Hit(Me, "Drudge", 10), Me);
        tracker.OnCombatEvent(Kill(Me, "Drudge"), Me);

        CombatTrackerRows.MonsterRow allRow =
            CombatTrackerRows.BuildMonsterRows(tracker, Me, sortAlphabetically: false)[1];

        // Right-aligned via left-padding, per docs/deviations.md — there is
        // no per-column alignment attribute in the markup.
        Assert.EndsWith("1", allRow.KillingBlows);
        Assert.StartsWith(" ", allRow.KillingBlows);
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

        IReadOnlyList<CombatTrackerRows.DamageRow> rows =
            CombatTrackerRows.BuildDamageRows(tracker, "Drudge", Me);

        CombatTrackerRows.DamageRow slashRow =
            rows.Single(r => r.Label == "Slash");
        Assert.Contains("40", slashRow.MeleeMissile);

        CombatTrackerRows.DamageRow totalRow =
            rows.Single(r => r.Label == "Total");
        Assert.Contains("40", totalRow.MeleeMissile);
        Assert.Contains("15", totalRow.Magic);
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

        IReadOnlyList<CombatTrackerRows.DamageRow> rows =
            CombatTrackerRows.BuildDamageRows(tracker, "Drudge", Me);
        CombatTrackerRows.DamageRow header = rows[0];

        Assert.Equal("Attacks", header.StatLabel);
        // 2 of 3 hit -> 66.666...% rounds to 67%.
        Assert.Contains("3", header.StatValue);
        Assert.Contains("67%", header.StatValue);
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

        IReadOnlyList<CombatTrackerRows.DamageRow> rows =
            CombatTrackerRows.BuildDamageRows(tracker, "Drudge", Me);
        CombatTrackerRows.DamageRow typelessRow =
            rows.Single(r => r.Label == "Typeless");

        Assert.Equal("Evades", typelessRow.StatLabel);
        // 4 melee defends (3 hits + 1 evade), 1 evaded -> 25%.
        Assert.Contains("4", typelessRow.StatValue);
        Assert.Contains("25%", typelessRow.StatValue);
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
