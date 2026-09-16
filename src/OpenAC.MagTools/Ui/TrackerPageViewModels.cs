using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Trackers.Combat;
using OpenAC.MagTools.Trackers.Corpse;
using OpenAC.MagTools.Trackers.Equipment;
using OpenAC.MagTools.Trackers.Inventory;
using OpenAC.MagTools.Trackers.Player;

namespace OpenAC.MagTools.Ui;

/// <summary>
/// Shared plumbing for a page whose list is filled by a tracker: a remembered
/// selection and a no-op row action by default.
/// </summary>
public abstract class ListPageViewModel
{
    protected ListPageViewModel()
    {
        Select = index =>
        {
            SelectedRow = index;
            // Virtual dispatch happens at call time, after the derived
            // class's own field initializers have already run -- unlike a
            // lambda captured through a base(...) constructor argument
            // (evaluated before any derived field initializer), this lets an
            // override safely read per-instance state such as a cached rows
            // builder (M5).
            OnRowSelected(index);
        };
        RowIconClicked = static _ => { };
    }

    public int SelectedRow { get; private set; } = -1;

    public Action<int> Select { get; }

    /// <summary>
    /// Called after <see cref="SelectedRow"/> is updated. A derived page
    /// overrides this to also drive host selection (<c>Selection.Select</c>)
    /// using the SAME row projection its display properties read, not a
    /// freshly built one.
    /// </summary>
    protected virtual void OnRowSelected(int index)
    {
    }

    /// <summary>Icon columns must declare a row action even when inert.</summary>
    public Action<int> RowIconClicked { get; }
}

/// <summary>Trackers → Mana, filled by P5's <see cref="EquipmentTrackerHost"/>.</summary>
public sealed class ManaPageViewModel : ListPageViewModel
{
    private readonly Setting<bool> _autoRecharge;
    private readonly EquipmentTrackerHost? _equipmentTrackerHost;
    private readonly IPluginHost? _host;
    private readonly TimeProvider _timeProvider;

    private uint[] _itemIcons = [];
    private string[] _itemNames = [];
    private uint[] _stateIcons = [];
    private string[] _itemMana = [];
    private string[] _itemTime = [];
    private string _totalText = "Mana needed: 0";
    private string _unretainedTotalText = "Unretained Items: 0";
    private bool _dirty = true;
    private Action? _onChanged;
    private bool _subscribed;

    public ManaPageViewModel(
        SettingsManager settings,
        EquipmentTrackerHost? equipmentTrackerHost = null,
        IPluginHost? host = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _autoRecharge = settings.ManaManagement.AutoRecharge;
        _equipmentTrackerHost = equipmentTrackerHost;
        _host = host;
        _timeProvider = timeProvider ?? TimeProvider.System;
        ToggleRecharge = () => _autoRecharge.Value = !_autoRecharge.Value;

        Resubscribe();
    }

    public void Dispose()
    {
        if (_equipmentTrackerHost is null || !_subscribed)
            return;
        _subscribed = false;
        _equipmentTrackerHost.Tracker.Changed -= _onChanged!;
    }

    public void Resubscribe()
    {
        if (_equipmentTrackerHost is null || _subscribed)
            return;
        _subscribed = true;
        _onChanged = () => _dirty = true;
        _equipmentTrackerHost.Tracker.Changed += _onChanged;
    }

    /// <summary>
    /// M6: rebuilds the row arrays AND the two total-label strings together,
    /// gated on the same dirty flag — <see cref="TotalText"/> and
    /// <see cref="UnretainedTotalText"/> used to recompute (and allocate a
    /// fresh formatted string) on every single poll, dirty or not.
    /// </summary>
    private void RefreshIfDirty()
    {
        if (!_dirty || _equipmentTrackerHost is null || _host is null)
            return;
        _dirty = false;

        IReadOnlyList<ManaTrackerRows.Row> rows = ManaTrackerRows.Build(
            _equipmentTrackerHost.Tracker,
            _host.Automation.Spells,
            _host.Automation.Character.ActiveEnchantments,
            _timeProvider);

        int count = rows.Count;
        _itemIcons = new uint[count];
        _itemNames = new string[count];
        _stateIcons = new uint[count];
        _itemMana = new string[count];
        _itemTime = new string[count];
        for (int i = 0; i < count; i++)
        {
            ManaTrackerRows.Row row = rows[i];
            _itemIcons[i] = row.ItemIcon;
            _itemNames[i] = row.Name;
            _stateIcons[i] = row.StateIcon;
            _itemMana[i] = row.ManaText;
            _itemTime[i] = row.TimeText;
        }

        int needed = _equipmentTrackerHost.Tracker.ManaNeededToRefillItems(
            _host.Automation.Spells, _host.Automation.Character.ActiveEnchantments);
        _totalText = "Mana needed: " + needed.ToString(System.Globalization.CultureInfo.InvariantCulture);

        int unretained = _equipmentTrackerHost.Tracker.NumberOfUnretainedItems();
        _unretainedTotalText =
            "Unretained Items: " + unretained.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public IReadOnlyList<uint> ItemIcons { get { RefreshIfDirty(); return _itemIcons; } }
    public IReadOnlyList<string> ItemNames { get { RefreshIfDirty(); return _itemNames; } }
    public IReadOnlyList<uint> StateIcons { get { RefreshIfDirty(); return _stateIcons; } }
    public IReadOnlyList<string> ItemMana { get { RefreshIfDirty(); return _itemMana; } }
    public IReadOnlyList<string> ItemTime { get { RefreshIfDirty(); return _itemTime; } }

    public string TotalText { get { RefreshIfDirty(); return _totalText; } }

    public string UnretainedTotalText { get { RefreshIfDirty(); return _unretainedTotalText; } }

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

/// <summary>Trackers → Corpse, filled by P6's <see cref="CorpseTrackerHost"/>.</summary>
public sealed class CorpsePageViewModel : ListPageViewModel
{
    private readonly CorpseTrackerSettings _settings;
    private readonly CorpseTrackerHost? _corpseTrackerHost;
    private readonly IPluginHost? _host;

    private IReadOnlyList<CorpseRow> _cachedRows = [];
    private IReadOnlyList<string> _cachedTimes = [];
    private IReadOnlyList<string> _cachedNames = [];
    private IReadOnlyList<string> _cachedCoordinates = [];
    private bool _dirty = true;
    private Action<TrackedCorpse>? _onChanged;
    private bool _subscribed;

    public CorpsePageViewModel(
        SettingsManager settings,
        CorpseTrackerHost? corpseTrackerHost = null,
        IPluginHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings.CorpseTracker;
        _corpseTrackerHost = corpseTrackerHost;
        _host = host;

        ClearHistory = () => _corpseTrackerHost?.Tracker.ClearStats();
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

        Resubscribe();
    }

    public void Dispose()
    {
        if (_corpseTrackerHost is null || !_subscribed)
            return;
        _subscribed = false;
        _corpseTrackerHost.Tracker.ItemAdded -= _onChanged!;
        _corpseTrackerHost.Tracker.ItemChanged -= _onChanged!;
        _corpseTrackerHost.Tracker.ItemRemoved -= _onChanged!;
    }

    public void Resubscribe()
    {
        if (_corpseTrackerHost is null || _subscribed)
            return;
        _subscribed = true;
        _onChanged = _ => _dirty = true;
        _corpseTrackerHost.Tracker.ItemAdded += _onChanged;
        _corpseTrackerHost.Tracker.ItemChanged += _onChanged;
        _corpseTrackerHost.Tracker.ItemRemoved += _onChanged;
    }

    /// <summary>
    /// Cached until the tracker reports a change (LOW: this used to rebuild
    /// AND reallocate fresh Times/Names/Coordinates lists on every single
    /// poll, dirty or not).
    /// </summary>
    private IReadOnlyList<CorpseRow> Rows
    {
        get
        {
            if (!_dirty)
                return _cachedRows;
            _dirty = false;
            _cachedRows = _corpseTrackerHost is null ? [] : CorpseTrackerRows.Build(_corpseTrackerHost.Tracker);
            _cachedTimes = _cachedRows.Select(row => row.Time).ToList();
            _cachedNames = _cachedRows.Select(row => row.Name).ToList();
            _cachedCoordinates = _cachedRows.Select(row => row.Coords).ToList();
            return _cachedRows;
        }
    }

    /// <summary>Selects through the SAME row projection the display properties below read (M5).</summary>
    protected override void OnRowSelected(int index)
    {
        if (_host is null)
            return;
        IReadOnlyList<CorpseRow> rows = Rows;
        if (index >= 0 && index < rows.Count)
            _host.Selection.Select(rows[index].ObjectId);
    }

    public IReadOnlyList<string> Times { get { _ = Rows; return _cachedTimes; } }
    public IReadOnlyList<string> Names { get { _ = Rows; return _cachedNames; } }
    public IReadOnlyList<string> Coordinates { get { _ = Rows; return _cachedCoordinates; } }

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

/// <summary>Trackers → Player, filled by P6's <see cref="PlayerTrackerHost"/>.</summary>
public sealed class PlayerPageViewModel : ListPageViewModel
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private readonly PlayerTrackerSettings _settings;
    private readonly PlayerTrackerHost? _playerTrackerHost;
    private readonly IPluginHost? _host;
    private readonly Func<DateTime> _now;
    // Stateful across accesses: the 10-second rate-limited re-sort (see
    // PlayerTrackerRowsBuilder) needs its own clock, not a fresh one per call.
    private readonly PlayerTrackerRowsBuilder _rowsBuilder = new();

    private IReadOnlyList<PlayerRow> _cachedRows = [];
    private IReadOnlyList<string> _cachedTimes = [];
    private IReadOnlyList<string> _cachedNames = [];
    private IReadOnlyList<string> _cachedCoordinates = [];
    private bool _dirty = true;
    private DateTime _lastBuild = DateTime.MinValue;
    private Action<TrackedPlayer>? _onChanged;
    private bool _subscribed;

    public PlayerPageViewModel(
        SettingsManager settings,
        PlayerTrackerHost? playerTrackerHost = null,
        IPluginHost? host = null,
        Func<DateTime>? now = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings.PlayerTracker;
        _playerTrackerHost = playerTrackerHost;
        _host = host;
        _now = now ?? (() => DateTime.UtcNow);

        ClearHistory = () => _playerTrackerHost?.Tracker.ClearStats();
        ToggleEnabled = () => _settings.Enabled.Value = !_settings.Enabled.Value;
        TogglePersistent = () =>
            _settings.Persistent.Value = !_settings.Persistent.Value;

        Resubscribe();
    }

    public void Dispose()
    {
        if (_playerTrackerHost is null || !_subscribed)
            return;
        _subscribed = false;
        _playerTrackerHost.Tracker.ItemAdded -= _onChanged!;
        _playerTrackerHost.Tracker.ItemChanged -= _onChanged!;
        _playerTrackerHost.Tracker.ItemRemoved -= _onChanged!;
    }

    public void Resubscribe()
    {
        if (_playerTrackerHost is null || _subscribed)
            return;
        _subscribed = true;
        _onChanged = _ => _dirty = true;
        _playerTrackerHost.Tracker.ItemAdded += _onChanged;
        _playerTrackerHost.Tracker.ItemChanged += _onChanged;
        _playerTrackerHost.Tracker.ItemRemoved += _onChanged;
    }

    /// <summary>
    /// Cached until the tracker reports a change OR 10 seconds have passed
    /// since the last build (P6 re-review LOW: this used to call
    /// <see cref="PlayerTrackerRowsBuilder.Build"/> -- which allocates a
    /// fresh <c>Dictionary</c> plus a fresh <c>List&lt;PlayerRow&gt;</c> --
    /// AND re-project three more fresh lists (Times/Names/Coordinates), on
    /// every single property read, unconditionally). The 10-second fallback
    /// mirrors <see cref="PlayerTrackerRowsBuilder"/>'s own internal re-sort
    /// cadence: display order can go stale across that window even with no
    /// <c>ItemChanged</c> in between (a tracked player's <c>LastSeen</c>
    /// updates do fire ItemChanged today, but the fallback keeps this cache
    /// honest even if that ever stops being true).
    /// </summary>
    private IReadOnlyList<PlayerRow> Rows
    {
        get
        {
            DateTime now = _now();
            if (!_dirty && now - _lastBuild < RefreshInterval)
                return _cachedRows;

            _dirty = false;
            _lastBuild = now;
            _cachedRows = _playerTrackerHost is null ? [] : _rowsBuilder.Build(_playerTrackerHost.Tracker);
            _cachedTimes = _cachedRows.Select(row => row.Time).ToList();
            _cachedNames = _cachedRows.Select(row => row.Name).ToList();
            _cachedCoordinates = _cachedRows.Select(row => row.Coords).ToList();
            return _cachedRows;
        }
    }

    /// <summary>
    /// Selects through the SAME cached, rate-limited row projection the
    /// display properties below read (M5) -- a fresh
    /// <c>PlayerTrackerRowsBuilder</c> would reorder differently than what
    /// is currently on screen, selecting the wrong player.
    /// </summary>
    protected override void OnRowSelected(int index)
    {
        if (_host is null)
            return;
        IReadOnlyList<PlayerRow> rows = Rows;
        if (index >= 0 && index < rows.Count)
            _host.Selection.Select(rows[index].ObjectId);
    }

    public IReadOnlyList<string> Times { get { _ = Rows; return _cachedTimes; } }
    public IReadOnlyList<string> Names { get { _ = Rows; return _cachedNames; } }
    public IReadOnlyList<string> Coordinates { get { _ = Rows; return _cachedCoordinates; } }

    public Action ClearHistory { get; }

    public bool Enabled => _settings.Enabled.Value;
    public bool Persistent => _settings.Persistent.Value;

    public Action ToggleEnabled { get; }
    public Action TogglePersistent { get; }
}

/// <summary>
/// Trackers → Inv. Items, filled by P5's <see cref="InventoryTrackerHost"/>
/// (the consumables and profit/loss trackers) through
/// <see cref="InventoryTrackerRows"/>.
/// </summary>
public sealed class InventoryItemsPageViewModel : ListPageViewModel
{
    private readonly InventoryTrackerHost? _inventoryTrackerHost;

    private uint[] _icons = [];
    private string[] _names = [];
    private string[] _counts = [];
    private string[] _average5m = [];
    private string[] _average1h = [];
    private string[] _hours = [];
    private bool _dirty = true;
    private Action? _onConsumablesChanged;
    private Action? _onProfitLossChanged;
    private bool _subscribed;

    public InventoryItemsPageViewModel(InventoryTrackerHost? inventoryTrackerHost = null)
    {
        _inventoryTrackerHost = inventoryTrackerHost;
        Resubscribe();
    }

    public void Dispose()
    {
        if (_inventoryTrackerHost is null || !_subscribed)
            return;
        _subscribed = false;
        _inventoryTrackerHost.Consumables.Changed -= _onConsumablesChanged!;
        _inventoryTrackerHost.ProfitLoss.Changed -= _onProfitLossChanged!;
    }

    public void Resubscribe()
    {
        if (_inventoryTrackerHost is null || _subscribed)
            return;
        _subscribed = true;
        _onConsumablesChanged = () => _dirty = true;
        _onProfitLossChanged = () => _dirty = true;
        _inventoryTrackerHost.Consumables.Changed += _onConsumablesChanged;
        _inventoryTrackerHost.ProfitLoss.Changed += _onProfitLossChanged;
    }

    private void RefreshIfDirty()
    {
        if (!_dirty || _inventoryTrackerHost is null)
            return;
        _dirty = false;

        IReadOnlyList<InventoryTrackerRows.Row> rows = InventoryTrackerRows.Build(_inventoryTrackerHost);
        int count = rows.Count;
        _icons = new uint[count];
        _names = new string[count];
        _counts = new string[count];
        _average5m = new string[count];
        _average1h = new string[count];
        _hours = new string[count];
        for (int i = 0; i < count; i++)
        {
            InventoryTrackerRows.Row row = rows[i];
            _icons[i] = row.Icon;
            _names[i] = row.Name;
            _counts[i] = row.Count;
            _average5m[i] = row.Average5m;
            _average1h[i] = row.Average1h;
            _hours[i] = row.Hours;
        }
    }

    public IReadOnlyList<uint> Icons { get { RefreshIfDirty(); return _icons; } }
    public IReadOnlyList<string> Names { get { RefreshIfDirty(); return _names; } }
    public IReadOnlyList<string> Counts { get { RefreshIfDirty(); return _counts; } }
    public IReadOnlyList<string> Average5m { get { RefreshIfDirty(); return _average5m; } }
    public IReadOnlyList<string> Average1h { get { RefreshIfDirty(); return _average1h; } }
    public IReadOnlyList<string> Hours { get { RefreshIfDirty(); return _hours; } }
}
