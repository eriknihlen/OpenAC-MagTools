namespace OpenAC.MagTools.Trackers.Combat;

/// <summary>
/// Port of the original's <c>Trackers.Combat.CombatTracker</c> — aggregates
/// parsed combat/aetheria/cloak events into per-opponent
/// <see cref="CombatInfo"/>/<see cref="AetheriaInfo"/>/<see cref="CloakInfo"/>
/// records, keyed by (SourceName, TargetName), and feeds the two 60-minute
/// DPS windows.
/// </summary>
/// <remarks>
/// The original constructed its own <c>StandardTracker</c>/<c>AetheriaTracker</c>/
/// <c>CloakTracker</c> instances and subscribed to the chat event directly, so
/// two <c>CombatTracker</c> instances (current session + persistent) meant
/// the raw text was parsed twice. This port receives already-parsed
/// <see cref="CombatEventArgs"/>/<see cref="SurgeEventArgs"/> via
/// <see cref="OnCombatEvent"/>/<see cref="OnAetheriaSurge"/>/
/// <see cref="OnCloakSurge"/> — the plugin's single feed dispatcher parses
/// once and forwards the same event to both instances. See docs/deviations.md.
/// </remarks>
public sealed class CombatTracker(TimeProvider? timeProvider = null)
{
    private const int DpsRetentionMinutes = 60;

    private readonly List<CombatInfo> _combatInfos = [];
    private readonly List<AetheriaInfo> _aetheriaInfos = [];
    private readonly List<CloakInfo> _cloakInfos = [];

    private readonly ValueSnapShotGroup _damageOutgoing = new(DpsRetentionMinutes, timeProvider);
    private readonly ValueSnapShotGroup _damageIncoming = new(DpsRetentionMinutes, timeProvider);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>Raised whenever any list is cleared, updated, or (re)loaded.</summary>
    public event Action? Changed;

    public IReadOnlyList<CombatInfo> CombatInfos => _combatInfos;

    public IReadOnlyList<AetheriaInfo> AetheriaInfos => _aetheriaInfos;

    public IReadOnlyList<CloakInfo> CloakInfos => _cloakInfos;

    /// <summary>
    /// Records one already-parsed combat event. <paramref name="localPlayerName"/>
    /// gates recording to events that interact with the local player, same as
    /// the original's <c>SourceName != Me &amp;&amp; TargetName != Me</c> guard.
    /// </summary>
    public void OnCombatEvent(CombatEventArgs args, string localPlayerName)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(localPlayerName);

        if (args.SourceName != localPlayerName && args.TargetName != localPlayerName)
            return;

        CombatInfo? info = _combatInfos.Find(
            i => i.SourceName == args.SourceName && i.TargetName == args.TargetName);
        if (info is null)
        {
            info = new CombatInfo(args.SourceName, args.TargetName);
            _combatInfos.Add(info);
        }

        info.AddFromCombatEventArgs(args);

        if (args.DamageAmount > 0)
        {
            DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

            if (args.SourceName == localPlayerName)
                _damageOutgoing.AddSnapShot(now, args.DamageAmount, DpsRetentionMinutes);

            if (args.TargetName == localPlayerName)
                _damageIncoming.AddSnapShot(now, args.DamageAmount, DpsRetentionMinutes);
        }

        Changed?.Invoke();
    }

    public void OnAetheriaSurge(Aetheria.SurgeEventArgs args, string localPlayerName)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(localPlayerName);

        if (args.SourceName != localPlayerName)
            return;

        AetheriaInfo? info = _aetheriaInfos.Find(
            i => i.SourceName == args.SourceName && i.TargetName == args.TargetName);
        if (info is null)
        {
            info = new AetheriaInfo(args.SourceName, args.TargetName);
            _aetheriaInfos.Add(info);
        }

        info.AddFromSurgeEventArgs(args);
        Changed?.Invoke();
    }

    public void OnCloakSurge(Cloaks.SurgeEventArgs args, string localPlayerName)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(localPlayerName);

        if (args.SourceName != localPlayerName)
            return;

        CloakInfo? info = _cloakInfos.Find(
            i => i.SourceName == args.SourceName && i.TargetName == args.TargetName);
        if (info is null)
        {
            info = new CloakInfo(args.SourceName, args.TargetName);
            _cloakInfos.Add(info);
        }

        info.AddFromSurgeEventArgs(args);
        Changed?.Invoke();
    }

    public IReadOnlyList<CombatInfo> GetCombatInfos(string name)
        => _combatInfos.FindAll(i => i.SourceName == name || i.TargetName == name);

    public IReadOnlyList<AetheriaInfo> GetAetheriaInfos(string name)
        => _aetheriaInfos.FindAll(i => i.SourceName == name || i.TargetName == name);

    public IReadOnlyList<CloakInfo> GetCloakInfos(string name)
        => _cloakInfos.FindAll(i => i.SourceName == name || i.TargetName == name);

    public void ClearStats()
    {
        _combatInfos.Clear();
        _aetheriaInfos.Clear();
        _cloakInfos.Clear();
        Changed?.Invoke();
    }

    /// <summary>Replaces every list with what <see cref="CombatTrackerImporter"/> read.</summary>
    public void LoadStats(
        IReadOnlyList<CombatInfo> combatInfos,
        IReadOnlyList<AetheriaInfo> aetheriaInfos,
        IReadOnlyList<CloakInfo> cloakInfos)
    {
        _combatInfos.Clear();
        _combatInfos.AddRange(combatInfos);
        _aetheriaInfos.Clear();
        _aetheriaInfos.AddRange(aetheriaInfos);
        _cloakInfos.Clear();
        _cloakInfos.AddRange(cloakInfos);
        Changed?.Invoke();
    }

    /// <summary>The calculated amount over <paramref name="overPeriod"/> using the history of period.</summary>
    public double GetDamageGivenOverTime(TimeSpan historyPeriod, TimeSpan overPeriod)
        => GetOverTime(_damageOutgoing, historyPeriod, overPeriod);

    /// <summary>The calculated amount over <paramref name="overPeriod"/> using the history of period.</summary>
    public double GetDamageReceivedOverTime(TimeSpan historyPeriod, TimeSpan overPeriod)
        => GetOverTime(_damageIncoming, historyPeriod, overPeriod);

    private static double GetOverTime(
        ValueSnapShotGroup group, TimeSpan historyPeriod, TimeSpan overPeriod)
    {
        int total = group.GetValueTotal(historyPeriod, out TimeSpan actualHistoryPeriodUsed);
        if (total == 0)
            return 0;

        // Faithful to the original: a single very-recent sample makes
        // actualHistoryPeriodUsed exactly zero, which divides out to
        // +Infinity rather than a guarded 0 — the original never guarded
        // this either. It self-corrects within a second or two of real
        // play as later samples land, so it is left as-is rather than
        // "fixed" into a different (undocumented) behavior.
        return total * (overPeriod.TotalSeconds / actualHistoryPeriodUsed.TotalSeconds);
    }

    /// <summary>Rounded DPS-out readouts for the HUD (a later slice wires these up).</summary>
    public double DpsOut1m => GetDamageGivenOverTime(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));

    public double DpsOut5m => GetDamageGivenOverTime(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1));

    public double DpsOut1h => GetDamageGivenOverTime(TimeSpan.FromHours(1), TimeSpan.FromSeconds(1));

    public double DpsIn1m => GetDamageReceivedOverTime(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));

    public double DpsIn5m => GetDamageReceivedOverTime(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1));

    public double DpsIn1h => GetDamageReceivedOverTime(TimeSpan.FromHours(1), TimeSpan.FromSeconds(1));
}
