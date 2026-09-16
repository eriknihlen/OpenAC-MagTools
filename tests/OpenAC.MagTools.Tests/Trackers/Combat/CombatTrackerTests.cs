using OpenAC.MagTools.Trackers.Combat;
using ProdAetheria = OpenAC.MagTools.Trackers.Combat.Aetheria;
using ProdCloaks = OpenAC.MagTools.Trackers.Combat.Cloaks;

namespace OpenAC.MagTools.Tests.Trackers.Combat;

public sealed class CombatTrackerTests
{
    private const string Me = "Acdream";

    [Fact]
    public void EventsNotInvolvingTheLocalPlayerAreIgnored()
    {
        var tracker = new CombatTracker();

        tracker.OnCombatEvent(Hit("Monster A", "Monster B", 10), Me);

        Assert.Empty(tracker.CombatInfos);
    }

    [Fact]
    public void EventsInvolvingTheLocalPlayerAreRecordedAndAggregatedByPair()
    {
        var tracker = new CombatTracker();

        tracker.OnCombatEvent(Hit(Me, "Drudge", 10), Me);
        tracker.OnCombatEvent(Hit(Me, "Drudge", 20), Me);
        tracker.OnCombatEvent(Hit("Olthoi", Me, 5), Me);

        Assert.Equal(2, tracker.CombatInfos.Count);
        CombatInfo vsDrudge = Assert.Single(tracker.GetCombatInfos("Drudge"));
        Assert.Equal(30, vsDrudge.Damage);
    }

    [Fact]
    public void ChangedFiresOnEveryRecordedEvent()
    {
        var tracker = new CombatTracker();
        int changedCount = 0;
        tracker.Changed += () => changedCount++;

        tracker.OnCombatEvent(Hit(Me, "Drudge", 10), Me);
        tracker.OnCombatEvent(Hit("Monster A", "Monster B", 10), Me); // ignored, no Changed

        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void ClearStatsEmptiesEveryList()
    {
        var tracker = new CombatTracker();
        tracker.OnCombatEvent(Hit(Me, "Drudge", 10), Me);
        tracker.OnAetheriaSurge(
            new ProdAetheria.SurgeEventArgs(Me, Me, ProdAetheria.SurgeType.SurgeOfDestruction), Me);
        tracker.OnCloakSurge(
            new ProdCloaks.SurgeEventArgs(Me, Me, ProdCloaks.SurgeType.CloakedInSkill), Me);

        tracker.ClearStats();

        Assert.Empty(tracker.CombatInfos);
        Assert.Empty(tracker.AetheriaInfos);
        Assert.Empty(tracker.CloakInfos);
    }

    [Fact]
    public void AetheriaSurgeIsOnlyRecordedWhenTheLocalPlayerIsTheSource()
    {
        var tracker = new CombatTracker();

        // Third-person / unknown-source surge: never counted.
        tracker.OnAetheriaSurge(
            new ProdAetheria.SurgeEventArgs(string.Empty, "Drudge", ProdAetheria.SurgeType.SurgeOfAffliction), Me);
        Assert.Empty(tracker.AetheriaInfos);

        tracker.OnAetheriaSurge(
            new ProdAetheria.SurgeEventArgs(Me, Me, ProdAetheria.SurgeType.SurgeOfDestruction), Me);
        Assert.Single(tracker.AetheriaInfos);
    }

    [Fact]
    public void DpsWindowExtrapolatesTotalOverTheRequestedPeriod()
    {
        var clock = new FakeTimeProvider();
        var tracker = new CombatTracker(clock);

        tracker.OnCombatEvent(Hit(Me, "Drudge", 100), Me);
        clock.Advance(TimeSpan.FromSeconds(30));
        tracker.OnCombatEvent(Hit(Me, "Drudge", 200), Me);

        // Total over the last 60s = 300 (both snapshots). The oldest
        // included snapshot is 30s old, so extrapolating to a 1-second
        // window gives 300 * (1/30) = 10.
        double dps = tracker.GetDamageGivenOverTime(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
        Assert.Equal(10.0, dps, precision: 6);
    }

    [Fact]
    public void DpsWindowIsZeroWithNoDamageRecorded()
    {
        var tracker = new CombatTracker(new FakeTimeProvider());
        Assert.Equal(0, tracker.GetDamageGivenOverTime(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1)));
        Assert.Equal(0, tracker.GetDamageReceivedOverTime(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void IncomingDamageFeedsTheReceivedWindowNotTheGivenOne()
    {
        var clock = new FakeTimeProvider();
        var tracker = new CombatTracker(clock);

        tracker.OnCombatEvent(Hit("Drudge", Me, 60), Me);

        Assert.Equal(
            0, tracker.GetDamageGivenOverTime(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1)));
        Assert.True(
            tracker.GetDamageReceivedOverTime(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1)) > 0);
    }

    [Fact]
    public void FailedAttacksDoNotFeedTheDpsWindow()
    {
        var tracker = new CombatTracker(new FakeTimeProvider());

        tracker.OnCombatEvent(
            new CombatEventArgs(
                Me, "Drudge", AttackType.MeleeMissle, DamageElement.Unknown,
                IsFailedAttack: true, IsCriticalHit: false, IsOverpower: false,
                IsSneakAttack: false, IsRecklessness: false, IsKillingBlow: false, DamageAmount: 0),
            Me);

        Assert.Equal(0, tracker.GetDamageGivenOverTime(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1)));
    }

    private static CombatEventArgs Hit(string source, string target, int damage) => new(
        source, target, AttackType.MeleeMissle, DamageElement.Slash,
        IsFailedAttack: false, IsCriticalHit: false, IsOverpower: false,
        IsSneakAttack: false, IsRecklessness: false, IsKillingBlow: false, DamageAmount: damage);
}
