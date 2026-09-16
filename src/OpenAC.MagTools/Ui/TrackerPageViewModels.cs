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
/// fills the two Current/Persistent monster+damage lists; row strings are
/// rebuilt lazily (only when the underlying tracker fires
/// <see cref="Trackers.Combat.CombatTracker.Changed"/>, or the selection/sort
/// toggle changes) so a per-frame binding poll never recomputes anything —
/// see the class remarks on <see cref="Trackers.Combat.CombatTrackerRows"/>
/// for the row-layout deviation from the original's 5-column widget.
/// </summary>
public sealed class CombatPageViewModel
{
    private readonly CombatTrackerSettings _settings;
    private readonly CombatTrackerHost? _combatTrackerHost;
    private readonly IPluginHost? _host;

    private IReadOnlyList<CombatTrackerRows.MonsterRow> _currentMonsterRows = [];
    private IReadOnlyList<string> _currentDamageRows = [];
    private IReadOnlyList<CombatTrackerRows.MonsterRow> _persistentMonsterRows = [];
    private IReadOnlyList<string> _persistentDamageRows = [];
    private bool _currentDirty = true;
    private bool _persistentDirty = true;

    public CombatPageViewModel(
        SettingsManager settings, CombatTrackerHost? combatTrackerHost = null, IPluginHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings.CombatTracker;
        _combatTrackerHost = combatTrackerHost;
        _host = host;

        if (_combatTrackerHost is not null)
        {
            _combatTrackerHost.Current.Changed += () => _currentDirty = true;
            _combatTrackerHost.Persistent.Changed += () => _persistentDirty = true;
        }

        SelectCurrentMonster = index =>
        {
            CurrentMonsterSelectedRow = NormalizeMonsterSelection(index);
            CurrentDamageSelectedRow = -1;
        };
        SelectCurrentDamage = index => CurrentDamageSelectedRow = index;
        SelectPersistentMonster = index =>
        {
            PersistentMonsterSelectedRow = NormalizeMonsterSelection(index);
            PersistentDamageSelectedRow = -1;
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

    // Row 0 (header) always redirects to row 1 ("All") — same as the
    // original's monsterList_Click: "if (row == 0) row = 1;".
    private static int NormalizeMonsterSelection(int index) => index <= 0 ? 1 : index;

    private string LocalPlayerName => _host?.Automation.Character.Name ?? string.Empty;

    private void RefreshCurrentIfDirty()
    {
        if (!_currentDirty || _combatTrackerHost is null)
            return;
        _currentDirty = false;
        _currentMonsterRows = CombatTrackerRows.BuildMonsterRows(
            _combatTrackerHost.Current, LocalPlayerName, _settings.SortAlphabetically.Value);
        _currentDamageRows = BuildDamageRows(_currentMonsterRows, CurrentMonsterSelectedRow, _combatTrackerHost.Current);
    }

    private void RefreshPersistentIfDirty()
    {
        if (!_persistentDirty || _combatTrackerHost is null)
            return;
        _persistentDirty = false;
        _persistentMonsterRows = CombatTrackerRows.BuildMonsterRows(
            _combatTrackerHost.Persistent, LocalPlayerName, _settings.SortAlphabetically.Value);
        _persistentDamageRows = BuildDamageRows(
            _persistentMonsterRows, PersistentMonsterSelectedRow, _combatTrackerHost.Persistent);
    }

    private IReadOnlyList<string> BuildDamageRows(
        IReadOnlyList<CombatTrackerRows.MonsterRow> monsterRows, int selectedRow, CombatTracker tracker)
    {
        int row = NormalizeMonsterSelection(selectedRow < 0 ? 1 : selectedRow);
        string? targetName = row < monsterRows.Count ? monsterRows[row].TargetName : LocalPlayerName;
        return CombatTrackerRows.BuildDamageRows(tracker, targetName, LocalPlayerName);
    }

    public IReadOnlyList<string> CurrentMonsterRows
    {
        get
        {
            RefreshCurrentIfDirty();
            return [.. _currentMonsterRows.Select(static row => row.Text)];
        }
    }

    public IReadOnlyList<string> CurrentDamageRows
    {
        get
        {
            RefreshCurrentIfDirty();
            return _currentDamageRows;
        }
    }

    public IReadOnlyList<string> PersistentMonsterRows
    {
        get
        {
            RefreshPersistentIfDirty();
            return [.. _persistentMonsterRows.Select(static row => row.Text)];
        }
    }

    public IReadOnlyList<string> PersistentDamageRows
    {
        get
        {
            RefreshPersistentIfDirty();
            return _persistentDamageRows;
        }
    }

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
