using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Trackers.Combat;
using OpenAC.MagTools.Ui;

namespace OpenAC.MagTools.Tests.Ui;

public sealed class CombatPageViewModelTests
{
    private const string Me = "Acdream";

    private static (FakeHost Host, CombatTrackerHost CombatHost, CombatPageViewModel Vm) Build()
    {
        var host = new FakeHost();
        host.Automation.Character.Name = Me;
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var combatHost = new CombatTrackerHost(host, new ChatOutput(host), settings);
        var vm = new CombatPageViewModel(settings, combatHost, host);
        return (host, combatHost, vm);
    }

    private static CombatEventArgs Hit(string source, string target, int damage) => new(
        source, target, AttackType.MeleeMissle, DamageElement.Slash,
        IsFailedAttack: false, IsCriticalHit: false, IsOverpower: false,
        IsSneakAttack: false, IsRecklessness: false, IsKillingBlow: false, DamageAmount: damage);

    [Fact]
    public void HeaderAndAllRowsAreAlwaysPresent()
    {
        (_, _, CombatPageViewModel vm) = Build();

        Assert.Equal("", vm.CurrentMonsterNames[0]);
        Assert.Equal("KB's", vm.CurrentMonsterKillingBlows[0]);
        Assert.Equal("All", vm.CurrentMonsterNames[1]);
    }

    [Fact]
    public void SelectingRowZeroRedirectsToTheAllRow()
    {
        (_, _, CombatPageViewModel vm) = Build();

        vm.SelectCurrentMonster(0);

        Assert.Equal(1, vm.CurrentMonsterSelectedRow);
    }

    [Fact]
    public void SelectingAMonsterRowRefreshesTheDamageListForThatMonster()
    {
        (FakeHost host, CombatTrackerHost combatHost, CombatPageViewModel vm) = Build();
        combatHost.Current.OnCombatEvent(Hit(Me, "Drudge", 40), Me);
        combatHost.Current.OnCombatEvent(Hit(Me, "Olthoi", 15), Me);

        // Force the initial (dirty) build before selecting, same as a real
        // binding read would.
        _ = vm.CurrentMonsterNames;
        int drudgeRow = vm.CurrentMonsterNames.ToList().IndexOf("Drudge");
        int olthoiRow = vm.CurrentMonsterNames.ToList().IndexOf("Olthoi");
        Assert.True(drudgeRow > 0);
        Assert.True(olthoiRow > 0);

        // Both hits are OUTGOING (Me is the source), so the given-damage sum
        // shows up in the damage list's "Total" row stat VALUE column (the
        // Mel/Msl/Magic columns and the per-element rows only ever break out
        // damage RECEIVED FROM this opponent).
        vm.SelectCurrentMonster(drudgeRow);
        string totalForDrudge = ZipLabel(vm.CurrentDamageLabels, vm.CurrentDamageStatValues, "Total");
        Assert.Contains("40", totalForDrudge);

        // Before the fix (H2), the damage columns were only rebuilt when the
        // TRACKER fired Changed — selecting a different monster row alone
        // left the previous selection's damage numbers on screen.
        vm.SelectCurrentMonster(olthoiRow);
        string totalForOlthoi = ZipLabel(vm.CurrentDamageLabels, vm.CurrentDamageStatValues, "Total");
        Assert.Contains("15", totalForOlthoi);
        Assert.DoesNotContain("40", totalForOlthoi);
    }

    [Fact]
    public void SortTogglePreservesRebuildOnNextAccess()
    {
        (_, CombatTrackerHost combatHost, CombatPageViewModel vm) = Build();
        combatHost.Current.OnCombatEvent(Hit(Me, "Zebra", 1), Me);
        combatHost.Current.OnCombatEvent(Hit(Me, "Apple", 1), Me);

        _ = vm.CurrentMonsterNames; // build once, unsorted
        Assert.Equal("Zebra", vm.CurrentMonsterNames[2]);

        vm.ToggleSortAlphabetically();

        Assert.Equal("Apple", vm.CurrentMonsterNames[2]);
        Assert.Equal("Zebra", vm.CurrentMonsterNames[3]);
    }

    [Fact]
    public void ColumnGettersReturnTheSameCachedInstanceUntilDirty()
    {
        (_, CombatTrackerHost combatHost, CombatPageViewModel vm) = Build();
        combatHost.Current.OnCombatEvent(Hit(Me, "Drudge", 10), Me);

        IReadOnlyList<string> first = vm.CurrentMonsterNames;
        IReadOnlyList<string> second = vm.CurrentMonsterNames;

        Assert.Same(first, second);
    }

    [Fact]
    public void DisposeUnsubscribesFromTheTrackerChangedEvent()
    {
        (_, CombatTrackerHost combatHost, CombatPageViewModel vm) = Build();
        // Consume the initial (always-dirty-on-construction) build first, so
        // the assertion below is testing the SUBSCRIPTION, not just the
        // one-time startup refresh every instance gets regardless of it.
        _ = vm.CurrentMonsterNames;

        vm.Dispose();
        combatHost.Current.OnCombatEvent(Hit(Me, "Drudge", 10), Me);

        // Without a live subscription the cached column set never gets
        // marked dirty again, so the new opponent never shows up.
        Assert.DoesNotContain("Drudge", vm.CurrentMonsterNames);
    }

    [Fact]
    public void ResubscribeReversesDispose()
    {
        (_, CombatTrackerHost combatHost, CombatPageViewModel vm) = Build();

        vm.Dispose();
        vm.Resubscribe();
        combatHost.Current.OnCombatEvent(Hit(Me, "Drudge", 10), Me);

        Assert.Contains("Drudge", vm.CurrentMonsterNames);
    }

    private static string ZipLabel(
        IReadOnlyList<string> labels, IReadOnlyList<string> values, string label)
    {
        int index = labels.ToList().IndexOf(label);
        Assert.True(index >= 0);
        return values[index];
    }
}
