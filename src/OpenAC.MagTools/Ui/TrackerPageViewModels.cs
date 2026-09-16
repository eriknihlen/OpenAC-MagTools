using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Trackers.Combat;

namespace OpenAC.MagTools.Ui;

/// <summary>
/// Shared plumbing for a page whose list is filled by a tracker that has not
/// shipped yet: an empty list, a remembered selection and a no-op row action.
/// </summary>
public abstract class ListPageViewModel
{
    protected ListPageViewModel()
    {
        Select = index => SelectedRow = index;
        RowIconClicked = static _ => { };
    }

    public int SelectedRow { get; private set; } = -1;

    public Action<int> Select { get; }

    /// <summary>Icon columns must declare a row action even when inert.</summary>
    public Action<int> RowIconClicked { get; }
}

/// <summary>Trackers → Mana. TODO(P5): the equipment/mana tracker fills this.</summary>
public sealed class ManaPageViewModel : ListPageViewModel
{
    private readonly Setting<bool> _autoRecharge;

    public ManaPageViewModel(SettingsManager settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _autoRecharge = settings.ManaManagement.AutoRecharge;
        ToggleRecharge = () => _autoRecharge.Value = !_autoRecharge.Value;
    }

    public IReadOnlyList<uint> ItemIcons { get; } = [];
    public IReadOnlyList<string> ItemNames { get; } = [];
    public IReadOnlyList<uint> StateIcons { get; } = [];
    public IReadOnlyList<string> ItemMana { get; } = [];
    public IReadOnlyList<string> ItemTime { get; } = [];

    public string TotalText => "Mana needed: 0";

    public string UnretainedTotalText => "Unretained Items: 0";

    public bool RechargeEnabled => _autoRecharge.Value;

    public Action ToggleRecharge { get; }
}

/// <summary>
/// Trackers → Combat. P4's <see cref="Trackers.Combat.CombatTrackerHost"/>
/// fills the two Current/Persistent monster+damage lists; both are real
/// multi-column lists (see <see cref="Trackers.Combat.CombatTrackerRows"/>),
/// so this view model projects each column into its own cached list.
/// Rebuilds happen lazily — only when the underlying tracker fires
/// <see cref="Trackers.Combat.CombatTracker.Changed"/>, the sort toggle
/// changes, or the monster selection changes (which row the damage list
/// aggregates for) — so a per-frame binding poll never recomputes or
/// reallocates anything.
/// </summary>
public sealed class CombatPageViewModel
{
    private readonly CombatTrackerSettings _settings;
    private readonly CombatTrackerHost? _combatTrackerHost;
    private readonly IPluginHost? _host;

    private readonly Action _onCurrentTrackerChanged;
    private readonly Action _onPersistentTrackerChanged;

    private CombatColumns _current = CombatColumns.Empty;
    private CombatColumns _persistent = CombatColumns.Empty;
    private bool _currentDirty = true;
    private bool _persistentDirty = true;
    private bool _subscribed;

    public CombatPageViewModel(
        SettingsManager settings, CombatTrackerHost? combatTrackerHost = null, IPluginHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings.CombatTracker;
        _combatTrackerHost = combatTrackerHost;
        _host = host;

        _onCurrentTrackerChanged = () => _currentDirty = true;
        _onPersistentTrackerChanged = () => _persistentDirty = true;

        Resubscribe();

        SelectCurrentMonster = index =>
        {
            CurrentMonsterSelectedRow = NormalizeMonsterSelection(index);
            CurrentDamageSelectedRow = -1;
            // The damage list aggregates whichever monster row is now
            // selected, so it must be rebuilt even though the tracker's own
            // data has not changed.
            _currentDirty = true;
        };
        SelectCurrentDamage = index => CurrentDamageSelectedRow = index;
        SelectPersistentMonster = index =>
        {
            PersistentMonsterSelectedRow = NormalizeMonsterSelection(index);
            PersistentDamageSelectedRow = -1;
            _persistentDirty = true;
        };
        SelectPersistentDamage = index => PersistentDamageSelectedRow = index;

        ClearCurrentStats = () => _combatTrackerHost?.ClearCurrent();
        ExportCurrentStats = () => _combatTrackerHost?.ExportCurrent();
        ClearPersistentStats = () => _combatTrackerHost?.ClearPersistent();

        ToggleExportOnLogOff = () =>
            _settings.ExportOnLogOff.Value = !_settings.ExportOnLogOff.Value;
        TogglePersistent = () =>
            _settings.Persistent.Value = !_settings.Persistent.Value;
        ToggleSortAlphabetically = () =>
        {
            _settings.SortAlphabetically.Value = !_settings.SortAlphabetically.Value;
            _currentDirty = true;
            _persistentDirty = true;
        };
    }

    /// <summary>
    /// Unsubscribes from the tracker <c>Changed</c> events, so a
    /// <c>Disable()</c> that outlives this instance does not leak them.
    /// Safe to call even when this instance was built without a
    /// <see cref="CombatTrackerHost"/>, or was already disposed.
    /// </summary>
    public void Dispose()
    {
        if (_combatTrackerHost is null || !_subscribed)
            return;
        _subscribed = false;
        _combatTrackerHost.Current.Changed -= _onCurrentTrackerChanged;
        _combatTrackerHost.Persistent.Changed -= _onPersistentTrackerChanged;
    }

    /// <summary>Reverses <see cref="Dispose"/> for a Disable()/Enable() cycle.</summary>
    public void Resubscribe()
    {
        if (_combatTrackerHost is null || _subscribed)
            return;
        _subscribed = true;
        _combatTrackerHost.Current.Changed += _onCurrentTrackerChanged;
        _combatTrackerHost.Persistent.Changed += _onPersistentTrackerChanged;
    }

    // Row 0 (header) always redirects to row 1 ("All") — same as the
    // original's monsterList_Click: "if (row == 0) row = 1;".
    private static int NormalizeMonsterSelection(int index) => index <= 0 ? 1 : index;

    private string LocalPlayerName => _host?.Automation.Character.Name ?? string.Empty;

    private void RefreshCurrentIfDirty()
    {
        if (!_currentDirty || _combatTrackerHost is null)
            return;
        _currentDirty = false;
        _current = CombatColumns.Build(
            _combatTrackerHost.Current, LocalPlayerName, _settings.SortAlphabetically.Value,
            CurrentMonsterSelectedRow);
    }

    private void RefreshPersistentIfDirty()
    {
        if (!_persistentDirty || _combatTrackerHost is null)
            return;
        _persistentDirty = false;
        _persistent = CombatColumns.Build(
            _combatTrackerHost.Persistent, LocalPlayerName, _settings.SortAlphabetically.Value,
            PersistentMonsterSelectedRow);
    }

    public IReadOnlyList<string> CurrentMonsterNames { get { RefreshCurrentIfDirty(); return _current.MonsterNames; } }

    public IReadOnlyList<string> CurrentMonsterKillingBlows { get { RefreshCurrentIfDirty(); return _current.MonsterKillingBlows; } }

    public IReadOnlyList<string> CurrentMonsterDamageReceived { get { RefreshCurrentIfDirty(); return _current.MonsterDamageReceived; } }

    public IReadOnlyList<string> CurrentMonsterDamageGiven { get { RefreshCurrentIfDirty(); return _current.MonsterDamageGiven; } }

    public IReadOnlyList<string> CurrentDamageLabels { get { RefreshCurrentIfDirty(); return _current.DamageLabels; } }

    public IReadOnlyList<string> CurrentDamageMeleeMissile { get { RefreshCurrentIfDirty(); return _current.DamageMeleeMissile; } }

    public IReadOnlyList<string> CurrentDamageMagic { get { RefreshCurrentIfDirty(); return _current.DamageMagic; } }

    public IReadOnlyList<string> CurrentDamageStatLabels { get { RefreshCurrentIfDirty(); return _current.DamageStatLabels; } }

    public IReadOnlyList<string> CurrentDamageStatValues { get { RefreshCurrentIfDirty(); return _current.DamageStatValues; } }

    public IReadOnlyList<string> PersistentMonsterNames { get { RefreshPersistentIfDirty(); return _persistent.MonsterNames; } }

    public IReadOnlyList<string> PersistentMonsterKillingBlows { get { RefreshPersistentIfDirty(); return _persistent.MonsterKillingBlows; } }

    public IReadOnlyList<string> PersistentMonsterDamageReceived { get { RefreshPersistentIfDirty(); return _persistent.MonsterDamageReceived; } }

    public IReadOnlyList<string> PersistentMonsterDamageGiven { get { RefreshPersistentIfDirty(); return _persistent.MonsterDamageGiven; } }

    public IReadOnlyList<string> PersistentDamageLabels { get { RefreshPersistentIfDirty(); return _persistent.DamageLabels; } }

    public IReadOnlyList<string> PersistentDamageMeleeMissile { get { RefreshPersistentIfDirty(); return _persistent.DamageMeleeMissile; } }

    public IReadOnlyList<string> PersistentDamageMagic { get { RefreshPersistentIfDirty(); return _persistent.DamageMagic; } }

    public IReadOnlyList<string> PersistentDamageStatLabels { get { RefreshPersistentIfDirty(); return _persistent.DamageStatLabels; } }

    public IReadOnlyList<string> PersistentDamageStatValues { get { RefreshPersistentIfDirty(); return _persistent.DamageStatValues; } }

    public int CurrentMonsterSelectedRow { get; private set; } = -1;
    public int CurrentDamageSelectedRow { get; private set; } = -1;
    public int PersistentMonsterSelectedRow { get; private set; } = -1;
    public int PersistentDamageSelectedRow { get; private set; } = -1;

    public Action<int> SelectCurrentMonster { get; }
    public Action<int> SelectCurrentDamage { get; }
    public Action<int> SelectPersistentMonster { get; }
    public Action<int> SelectPersistentDamage { get; }

    public Action ClearCurrentStats { get; }
    public Action ExportCurrentStats { get; }
    public Action ClearPersistentStats { get; }

    public bool ExportOnLogOff => _settings.ExportOnLogOff.Value;
    public bool Persistent => _settings.Persistent.Value;
    public bool SortAlphabetically => _settings.SortAlphabetically.Value;

    public Action ToggleExportOnLogOff { get; }
    public Action TogglePersistent { get; }
    public Action ToggleSortAlphabetically { get; }

    /// <summary>
    /// One tracker's fully-materialized column set: every
    /// <see cref="CombatTrackerRows.MonsterRow"/>/<see cref="CombatTrackerRows.DamageRow"/>
    /// column projected once into its own list, so a repeated get on any
    /// <see cref="CombatPageViewModel"/> column property returns the SAME
    /// list instance rather than re-projecting on every call.
    /// </summary>
    private readonly record struct CombatColumns(
        IReadOnlyList<string> MonsterNames,
        IReadOnlyList<string> MonsterKillingBlows,
        IReadOnlyList<string> MonsterDamageReceived,
        IReadOnlyList<string> MonsterDamageGiven,
        IReadOnlyList<string> DamageLabels,
        IReadOnlyList<string> DamageMeleeMissile,
        IReadOnlyList<string> DamageMagic,
        IReadOnlyList<string> DamageStatLabels,
        IReadOnlyList<string> DamageStatValues)
    {
        public static readonly CombatColumns Empty = new([], [], [], [], [], [], [], [], []);

        public static CombatColumns Build(
            CombatTracker tracker, string localPlayerName, bool sortAlphabetically, int selectedMonsterRow)
        {
            IReadOnlyList<CombatTrackerRows.MonsterRow> monsterRows =
                CombatTrackerRows.BuildMonsterRows(tracker, localPlayerName, sortAlphabetically);

            int row = NormalizeMonsterSelection(selectedMonsterRow < 0 ? 1 : selectedMonsterRow);
            string? targetName = row < monsterRows.Count ? monsterRows[row].TargetName : localPlayerName;
            IReadOnlyList<CombatTrackerRows.DamageRow> damageRows =
                CombatTrackerRows.BuildDamageRows(tracker, targetName, localPlayerName);

            int monsterCount = monsterRows.Count;
            var names = new string[monsterCount];
            var killingBlows = new string[monsterCount];
            var damageReceived = new string[monsterCount];
            var damageGiven = new string[monsterCount];
            for (int i = 0; i < monsterCount; i++)
            {
                CombatTrackerRows.MonsterRow monsterRow = monsterRows[i];
                names[i] = monsterRow.Name;
                killingBlows[i] = monsterRow.KillingBlows;
                damageReceived[i] = monsterRow.DamageReceived;
                damageGiven[i] = monsterRow.DamageGiven;
            }

            int damageCount = damageRows.Count;
            var labels = new string[damageCount];
            var meleeMissile = new string[damageCount];
            var magic = new string[damageCount];
            var statLabels = new string[damageCount];
            var statValues = new string[damageCount];
            for (int i = 0; i < damageCount; i++)
            {
                CombatTrackerRows.DamageRow damageRow = damageRows[i];
                labels[i] = damageRow.Label;
                meleeMissile[i] = damageRow.MeleeMissile;
                magic[i] = damageRow.Magic;
                statLabels[i] = damageRow.StatLabel;
                statValues[i] = damageRow.StatValue;
            }

            return new CombatColumns(
                names, killingBlows, damageReceived, damageGiven,
                labels, meleeMissile, magic, statLabels, statValues);
        }
    }
}

/// <summary>Trackers → Corpse. TODO(P6): the corpse tracker fills the list.</summary>
public sealed class CorpsePageViewModel : ListPageViewModel
{
    private readonly CorpseTrackerSettings _settings;

    public CorpsePageViewModel(SettingsManager settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings.CorpseTracker;

        ClearHistory = static () => { };
        ToggleEnabled = () => _settings.Enabled.Value = !_settings.Enabled.Value;
        TogglePersistent = () =>
            _settings.Persistent.Value = !_settings.Persistent.Value;
        ToggleTrackAll = () =>
            _settings.TrackAllCorpses.Value = !_settings.TrackAllCorpses.Value;
        ToggleTrackFellow = () =>
            _settings.TrackFellowCorpses.Value = !_settings.TrackFellowCorpses.Value;
        ToggleTrackPermitted = () =>
            _settings.TrackPermittedCorpses.Value =
                !_settings.TrackPermittedCorpses.Value;
    }

    public IReadOnlyList<string> Times { get; } = [];
    public IReadOnlyList<string> Names { get; } = [];
    public IReadOnlyList<string> Coordinates { get; } = [];

    public Action ClearHistory { get; }

    public bool Enabled => _settings.Enabled.Value;
    public bool Persistent => _settings.Persistent.Value;
    public bool TrackAll => _settings.TrackAllCorpses.Value;
    public bool TrackFellow => _settings.TrackFellowCorpses.Value;
    public bool TrackPermitted => _settings.TrackPermittedCorpses.Value;

    public Action ToggleEnabled { get; }
    public Action TogglePersistent { get; }
    public Action ToggleTrackAll { get; }
    public Action ToggleTrackFellow { get; }
    public Action ToggleTrackPermitted { get; }
}

/// <summary>Trackers → Player. TODO(P6): the player tracker fills the list.</summary>
public sealed class PlayerPageViewModel : ListPageViewModel
{
    private readonly PlayerTrackerSettings _settings;

    public PlayerPageViewModel(SettingsManager settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings.PlayerTracker;

        ClearHistory = static () => { };
        ToggleEnabled = () => _settings.Enabled.Value = !_settings.Enabled.Value;
        TogglePersistent = () =>
            _settings.Persistent.Value = !_settings.Persistent.Value;
    }

    public IReadOnlyList<string> Times { get; } = [];
    public IReadOnlyList<string> Names { get; } = [];
    public IReadOnlyList<string> Coordinates { get; } = [];

    public Action ClearHistory { get; }

    public bool Enabled => _settings.Enabled.Value;
    public bool Persistent => _settings.Persistent.Value;

    public Action ToggleEnabled { get; }
    public Action TogglePersistent { get; }
}

/// <summary>
/// Trackers → Inv. Items. TODO(P5): the consumables and profit/loss trackers
/// fill the profit block and the per-consumable rows.
/// </summary>
public sealed class InventoryItemsPageViewModel : ListPageViewModel
{
    public IReadOnlyList<uint> Icons { get; } = [];
    public IReadOnlyList<string> Names { get; } = [];
    public IReadOnlyList<string> Counts { get; } = [];
    public IReadOnlyList<string> Average5m { get; } = [];
    public IReadOnlyList<string> Average1h { get; } = [];
    public IReadOnlyList<string> Hours { get; } = [];
}
