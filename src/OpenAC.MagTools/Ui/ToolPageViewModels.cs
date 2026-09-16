using System.Text.RegularExpressions;
using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Inventory;
using OpenAC.MagTools.ItemInfo;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Ui;

/// <summary>
/// Tools -&gt; Inventory: the two clipboard buttons, the regex search box and
/// the result list with the selected item's info below it.
/// </summary>
/// <remarks>
/// Ports <c>InventoryToolsView</c>'s <c>InventorySearch_Change</c> and
/// <c>InventoryList_Click</c>: the search box re-filters every owned item's
/// formatted info line on each keystroke (case-insensitive regex; an invalid
/// pattern yields an empty result list rather than throwing), and clicking a
/// result row opens its container (if not the main pack) and shows its full
/// info text below.
/// </remarks>
public sealed class InventoryToolsPageViewModel : ListPageViewModel
{
    private readonly IPluginHost? _host;
    private readonly ItemInfoOnIdentSettings? _settings;
    private readonly ISpellCatalog? _spells;

    private List<PluginInventoryItem> _matches = [];

    /// <summary>Parameterless ctor for pre-P3 callers/tests that don't need live search.</summary>
    public InventoryToolsPageViewModel()
        : this(null, null, null)
    {
    }

    public InventoryToolsPageViewModel(
        IPluginHost? host,
        ItemInfoOnIdentSettings? settings,
        ISpellCatalog? spells)
    {
        _host = host;
        _settings = settings;
        _spells = spells;

        ClipboardWornEquipment = () => Exporter?.ExportToClipboard(ExportGroups.WornEquipment);
        ClipboardInventoryInfo = () => Exporter?.ExportToClipboard(ExportGroups.Inventory);
        SetSearchText = text =>
        {
            SearchText = text;
            RunSearch();
        };
        SelectResultRow = SelectRow;
        SelectItemInfoLine = index => ItemInfoSelectedRow = index;
    }

    public Action ClipboardWornEquipment { get; }
    public Action ClipboardInventoryInfo { get; }

    /// <summary>
    /// Set by the plugin composition root once the tick-scheduler-backed
    /// exporter exists (it is built in <c>Enable()</c>, after this view-model
    /// is constructed in <c>Initialize()</c>). Null before then and after
    /// <c>Disable()</c>, in which case the two clipboard buttons are no-ops.
    /// </summary>
    public InventoryExporter? Exporter { get; set; }

    /// <summary>The original seeded the box with this and cleared it on use.</summary>
    public string SearchText { get; private set; } = "regex search string";

    public Action<string> SetSearchText { get; }

    public IReadOnlyList<uint> ResultIcons { get; private set; } = [];
    public IReadOnlyList<string> ResultNames { get; private set; } = [];

    /// <summary>
    /// The result-row click action. Named separately from the inherited
    /// <see cref="ListPageViewModel.Select"/> (kept as pure selection-index
    /// state for the markup binding contract) because clicking a search
    /// result also opens the item's container and prints its info.
    /// </summary>
    public Action<int> SelectResultRow { get; }

    /// <summary>
    /// The selected item's info, one line per row. The original's item info
    /// box was multi-line text; the markup has no multiline label, so this is
    /// bound to a plain-text list instead.
    /// </summary>
    public IReadOnlyList<string> ItemInfoLines { get; private set; } = [];

    public int ItemInfoSelectedRow { get; private set; } = -1;

    public Action<int> SelectItemInfoLine { get; }

    private void RunSearch()
    {
        ItemInfoLines = [];
        ItemInfoSelectedRow = -1;

        if (_host is null || _settings is null || _spells is null)
        {
            ResultIcons = [];
            ResultNames = [];
            _matches = [];
            return;
        }

        Regex? regex;
        try
        {
            regex = new Regex(SearchText, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException)
        {
            // Invalid pattern -> empty result list, never throw.
            regex = null;
        }

        var icons = new List<uint>();
        var names = new List<string>();
        var matches = new List<PluginInventoryItem>();

        if (regex is not null)
        {
            IWorldObjectAutomation objects = _host.Automation.Objects;
            foreach (PluginInventoryItem inventoryItem in _host.Automation.Items.CaptureOwnedItems())
            {
                if (!objects.TryGet(inventoryItem.ObjectId, out PluginWorldObject worldObject))
                    continue;

                if (!objects.TryCaptureProperties(inventoryItem.ObjectId, out PluginItemProperties properties))
                    properties = ItemModel.EmptyProperties;
                var model = new ItemModel(worldObject, properties, inventoryItem);
                string info = ItemInfoFormatter.Format(model, _settings, _spells);

                if (!regex.IsMatch(info))
                    continue;

                icons.Add(worldObject.IconId);
                names.Add(worldObject.Name);
                matches.Add(inventoryItem);
            }
        }

        ResultIcons = icons;
        ResultNames = names;
        _matches = matches;
    }

    /// <summary>
    /// Ports <c>InventoryList_Click</c>: open the item's container if it is
    /// not the main pack, select the item, and show its info.
    /// </summary>
    private void SelectRow(int index)
    {
        Select(index);
        ItemInfoLines = [];
        ItemInfoSelectedRow = -1;

        if (_host is null || _settings is null || _spells is null)
            return;
        if (index < 0 || index >= _matches.Count)
            return;

        PluginInventoryItem inventoryItem = _matches[index];
        // MEDIUM-6 (P10 review): on this host an equipped item's
        // ContainerObjectId is whatever container it was equipped FROM (or
        // 0), never the player -- the same host-shape fact behind defect
        // 9's IsEquippedByMe fix -- so this plain container-id comparison
        // treated every equipped item as foreign and called Items.Use(0)
        // for it. An equipped item is directly on the character; never
        // open a container for it.
        bool isEquippedByMe = inventoryItem.IsEquipped
            && inventoryItem.WielderObjectId == _host.Automation.Character.ObjectId;
        if (!isEquippedByMe && inventoryItem.ContainerObjectId != _host.Automation.Character.ObjectId)
            _host.Automation.Items.Use(inventoryItem.ContainerObjectId);

        _host.Selection.Select(inventoryItem.ObjectId);

        IWorldObjectAutomation objects = _host.Automation.Objects;
        if (!objects.TryGet(inventoryItem.ObjectId, out PluginWorldObject worldObject))
            return;

        if (!objects.TryCaptureProperties(inventoryItem.ObjectId, out PluginItemProperties properties))
            properties = ItemModel.EmptyProperties;
        var model = new ItemModel(worldObject, properties, inventoryItem);
        ItemInfoLines = [ItemInfoFormatter.Format(model, _settings, _spells)];
    }
}

/// <summary>
/// Tools -&gt; Tinkering: Add Selected Item, the material picker, the
/// unidentified-salvage-material scan (Start/Stop), and the row list, backed
/// by P6's <see cref="Macros.TinkeringToolsHost"/>.
/// </summary>
public sealed class TinkeringPageViewModel : ListPageViewModel
{
    private readonly Macros.TinkeringToolsHost? _tinkeringHost;

    public TinkeringPageViewModel(Macros.TinkeringToolsHost? tinkeringHost = null)
    {
        _tinkeringHost = tinkeringHost;

        AddSelectedItem = () => _tinkeringHost?.AddSelectedItem();
        Start = () => _tinkeringHost?.StartScan(() => SelectedMaterial);
        Stop = () => _tinkeringHost?.StopScan();
        SelectMaterial = material => SelectedMaterial = material;
        SetMinimumPercent = text => MinimumPercentText = text;
        SetTargetTotalTinks = text => TargetTotalTinksText = text;
        DeleteRow = index => _tinkeringHost?.DeleteRow(index);

        Resubscribe();
    }

    protected override void OnRowSelected(int index) => _tinkeringHost?.SelectRow(index);

    public void Dispose()
    {
        if (_tinkeringHost is null || !_subscribed)
            return;
        _subscribed = false;
        _tinkeringHost.Changed -= _onChanged!;
    }

    public void Resubscribe()
    {
        if (_tinkeringHost is null || _subscribed)
            return;
        _subscribed = true;
        _onChanged = () => _dirty = true;
        _tinkeringHost.Changed += _onChanged;
    }

    public Action AddSelectedItem { get; }
    public Action Start { get; }
    public Action Stop { get; }

    /// <summary>The original's fixed material choice list.</summary>
    public IReadOnlyList<string> Materials { get; } = Macros.TinkeringToolsHost.Materials;

    public string SelectedMaterial { get; private set; } = Macros.TinkeringToolsHost.Materials[0];

    public Action<string> SelectMaterial { get; }

    public string MinimumPercentText { get; private set; } = "100";

    public Action<string> SetMinimumPercent { get; }

    /// <summary>
    /// Present in the original's layout but never read by any of its logic
    /// (<c>tinkeringTargetTotalTinks</c> is assigned from the mainView field
    /// and never referenced again in <c>TinkeringToolsView.cs</c>) — kept as
    /// an inert text field to match.
    /// </summary>
    public string TargetTotalTinksText { get; private set; } = string.Empty;

    public Action<string> SetTargetTotalTinks { get; }

    private Action? _onChanged;
    private bool _subscribed;
    private bool _dirty = true;
    private IReadOnlyList<string> _cachedCurrentMarks = [];
    private IReadOnlyList<uint> _cachedIcons = [];
    private IReadOnlyList<string> _cachedNames = [];
    private IReadOnlyList<string> _cachedWork = [];
    private IReadOnlyList<string> _cachedTinks = [];
    private IReadOnlyList<uint> _cachedDeleteIcons = [];

    /// <summary>
    /// Cached until <see cref="Macros.TinkeringToolsHost.Changed"/> reports a
    /// row add/delete/ident-fill (P6 re-review LOW: the event existed but was
    /// never subscribed to -- every property below re-derived a fresh list
    /// via LINQ on every single poll, dirty or not).
    /// </summary>
    private void RefreshIfDirty()
    {
        if (!_dirty)
            return;
        _dirty = false;

        IReadOnlyList<Macros.TinkeringRow> rows = _tinkeringHost?.Rows ?? [];
        _cachedCurrentMarks = rows.Select(static _ => string.Empty).ToList();
        _cachedIcons = rows.Select(row => row.IconId).ToList();
        _cachedNames = rows.Select(row => row.Name).ToList();
        _cachedWork = rows.Select(row => row.Work).ToList();
        _cachedTinks = rows.Select(row => row.Tinks).ToList();
        _cachedDeleteIcons = rows.Select(static _ => 0x60011F8u).ToList();
    }

    public IReadOnlyList<string> CurrentMarks { get { RefreshIfDirty(); return _cachedCurrentMarks; } }
    public IReadOnlyList<uint> Icons { get { RefreshIfDirty(); return _cachedIcons; } }
    public IReadOnlyList<string> Names { get { RefreshIfDirty(); return _cachedNames; } }
    public IReadOnlyList<string> Work { get { RefreshIfDirty(); return _cachedWork; } }
    public IReadOnlyList<string> Tinks { get { RefreshIfDirty(); return _cachedTinks; } }
    public IReadOnlyList<uint> DeleteIcons { get { RefreshIfDirty(); return _cachedDeleteIcons; } }

    public Action<int> DeleteRow { get; }
}

/// <summary>
/// Tools -&gt; Character and Tools -&gt; Server: the on-login, on-login-complete
/// and periodic command lists for one scope.
/// </summary>
/// <remarks>
/// One instance is bound to the account/server/character scope
/// (<see cref="Settings.SettingsScope.Character"/>), the other to the server
/// scope (<see cref="Settings.SettingsScope.Server"/>) -- see
/// <see cref="MainViewModel.CharacterCommands"/>/<see cref="MainViewModel.ServerCommands"/>.
/// <see cref="Bind"/> is called once per login (the scope path depends on the
/// live account/server/character, which is not known until then), reloading
/// all three lists from storage. Every Add/Move/Delete mutates the in-memory
/// list and immediately rewrites the whole node back to
/// <see cref="Settings.ScopedCommandStore"/>, mirroring the original's
/// "rewrite the whole list on every change" `LoginList`/`PeriodicCommandList`
/// click handlers. The row icons are the original's up/down/delete art.
/// </remarks>
/// <remarks>
/// L3/LOW-1, small cosmetics worth spelling out: a successful On-Login/
/// On-Login-Complete Add trims the typed command text (leading/trailing
/// whitespace only -- the command's own internal spacing is untouched)
/// before storing it and clears its text field back to empty. A successful
/// Periodic Add stores the command text VERBATIM (no trim, matching the
/// original -- see <see cref="AddPeriodic"/>'s remarks) and clears ONLY the
/// command text field; the interval/offset fields are left exactly as
/// typed so several commands can be added at the same interval/offset
/// without retyping them. A refused Add (blank command text, or -- for the
/// periodic list -- an invalid interval/offset, M2) leaves EVERY typed
/// field exactly as entered, so the user can fix just the one bad field.
/// Move swaps two adjacent rows AND moves the selection with the swapped
/// row (so "move up" keeps the same logical row selected, not the row now
/// sitting in its old slot). Delete clears the selection when the deleted
/// row WAS selected, or shifts the selection index down by one when a row
/// ABOVE the selection was deleted, so the same logical row (if any) stays
/// selected across the delete.
/// </remarks>
public sealed class ScopedCommandsPageViewModel
{
    public const uint UpIconId = 0x060028FCu;
    public const uint DownIconId = 0x060028FDu;
    public const uint DeleteIconId = 0x060011F8u;

    private readonly Settings.ScopedCommandStore? _store;
    private string _scopePath = string.Empty;

    private List<string> _loginCommands = [];
    private List<string> _loginCompleteCommands = [];
    private List<Settings.PeriodicCommand> _periodicCommands = [];

    /// <summary>Parameterless ctor for pre-P8 callers/tests that don't need live storage.</summary>
    public ScopedCommandsPageViewModel()
        : this(null)
    {
    }

    public ScopedCommandsPageViewModel(Settings.ScopedCommandStore? store)
    {
        _store = store;

        SetLoginText = text => LoginText = text;
        SetLoginCompleteText = text => LoginCompleteText = text;
        SetPeriodicText = text => PeriodicText = text;
        SetPeriodicInterval = text => PeriodicIntervalText = text;
        SetPeriodicOffset = text => PeriodicOffsetText = text;

        AddLoginCommand = AddLogin;
        AddLoginCompleteCommand = AddLoginComplete;
        AddPeriodicCommand = AddPeriodic;

        MoveLoginCommandUp = index => MoveLogin(index, -1);
        MoveLoginCommandDown = index => MoveLogin(index, 1);
        DeleteLoginCommand = DeleteLogin;
        MoveLoginCompleteCommandUp = index => MoveLoginComplete(index, -1);
        MoveLoginCompleteCommandDown = index => MoveLoginComplete(index, 1);
        DeleteLoginCompleteCommand = DeleteLoginComplete;
        DeletePeriodicCommand = DeletePeriodic;

        SelectLoginRow = index => LoginSelectedRow = index;
        SelectLoginCompleteRow = index => LoginCompleteSelectedRow = index;
        SelectPeriodicRow = index => PeriodicSelectedRow = index;
    }

    /// <summary>
    /// Rebinds this page to a scope path (called once per login) and reloads
    /// all three lists from storage.
    /// </summary>
    public void Bind(string scopePath)
    {
        ArgumentNullException.ThrowIfNull(scopePath);
        _scopePath = scopePath;
        LoginSelectedRow = -1;
        LoginCompleteSelectedRow = -1;
        PeriodicSelectedRow = -1;
        Reload();
    }

    private void Reload()
    {
        if (_store is null || _scopePath.Length == 0)
        {
            _loginCommands = [];
            _loginCompleteCommands = [];
            _periodicCommands = [];
            return;
        }

        _loginCommands = [.. _store.GetOnLoginCommands(_scopePath)];
        _loginCompleteCommands = [.. _store.GetOnLoginCompleteCommands(_scopePath)];
        _periodicCommands = [.. _store.GetPeriodicCommands(_scopePath)];
    }

    private bool CanEdit => _store is not null && _scopePath.Length > 0;

    private static IReadOnlyList<uint> RepeatIcon(uint icon, int count)
    {
        if (count <= 0)
            return [];
        var icons = new uint[count];
        Array.Fill(icons, icon);
        return icons;
    }

    // ---- On-Login -------------------------------------------------------

    public string LoginText { get; private set; } = string.Empty;
    public Action<string> SetLoginText { get; }
    public Action AddLoginCommand { get; }
    public IReadOnlyList<string> LoginCommands => _loginCommands;
    public IReadOnlyList<uint> LoginUpIcons => RepeatIcon(UpIconId, _loginCommands.Count);
    public IReadOnlyList<uint> LoginDownIcons => RepeatIcon(DownIconId, _loginCommands.Count);
    public IReadOnlyList<uint> LoginDeleteIcons => RepeatIcon(DeleteIconId, _loginCommands.Count);
    public Action<int> MoveLoginCommandUp { get; }
    public Action<int> MoveLoginCommandDown { get; }
    public Action<int> DeleteLoginCommand { get; }
    public int LoginSelectedRow { get => _loginSelectedRowField; private set => _loginSelectedRowField = value; }
    public Action<int> SelectLoginRow { get; }

    private void AddLogin()
    {
        if (!CanEdit)
            return;
        string text = LoginText.Trim();
        if (text.Length == 0)
            return;

        _loginCommands.Add(text);
        _store!.SetOnLoginCommands(_scopePath, _loginCommands);
        LoginText = string.Empty;
    }

    private void MoveLogin(int index, int direction)
    {
        if (!CanEdit)
            return;
        int target = index + direction;
        if (index < 0 || index >= _loginCommands.Count || target < 0 || target >= _loginCommands.Count)
            return;

        (_loginCommands[index], _loginCommands[target]) = (_loginCommands[target], _loginCommands[index]);
        _store!.SetOnLoginCommands(_scopePath, _loginCommands);
        LoginSelectedRow = target;
    }

    private void DeleteLogin(int index)
    {
        if (!CanEdit || index < 0 || index >= _loginCommands.Count)
            return;

        _loginCommands.RemoveAt(index);
        _store!.SetOnLoginCommands(_scopePath, _loginCommands);
        AdjustSelectionAfterDelete(ref RefLoginSelectedRow(), index);
    }

    // ---- On-Login-Complete ------------------------------------------------

    public string LoginCompleteText { get; private set; } = string.Empty;
    public Action<string> SetLoginCompleteText { get; }
    public Action AddLoginCompleteCommand { get; }
    public IReadOnlyList<string> LoginCompleteCommands => _loginCompleteCommands;
    public IReadOnlyList<uint> LoginCompleteUpIcons => RepeatIcon(UpIconId, _loginCompleteCommands.Count);
    public IReadOnlyList<uint> LoginCompleteDownIcons => RepeatIcon(DownIconId, _loginCompleteCommands.Count);
    public IReadOnlyList<uint> LoginCompleteDeleteIcons => RepeatIcon(DeleteIconId, _loginCompleteCommands.Count);
    public Action<int> MoveLoginCompleteCommandUp { get; }
    public Action<int> MoveLoginCompleteCommandDown { get; }
    public Action<int> DeleteLoginCompleteCommand { get; }
    public int LoginCompleteSelectedRow { get => _loginCompleteSelectedRowField; private set => _loginCompleteSelectedRowField = value; }
    public Action<int> SelectLoginCompleteRow { get; }

    private void AddLoginComplete()
    {
        if (!CanEdit)
            return;
        string text = LoginCompleteText.Trim();
        if (text.Length == 0)
            return;

        _loginCompleteCommands.Add(text);
        _store!.SetOnLoginCompleteCommands(_scopePath, _loginCompleteCommands);
        LoginCompleteText = string.Empty;
    }

    private void MoveLoginComplete(int index, int direction)
    {
        if (!CanEdit)
            return;
        int target = index + direction;
        if (index < 0 || index >= _loginCompleteCommands.Count
            || target < 0 || target >= _loginCompleteCommands.Count)
            return;

        (_loginCompleteCommands[index], _loginCompleteCommands[target]) =
            (_loginCompleteCommands[target], _loginCompleteCommands[index]);
        _store!.SetOnLoginCompleteCommands(_scopePath, _loginCompleteCommands);
        LoginCompleteSelectedRow = target;
    }

    private void DeleteLoginComplete(int index)
    {
        if (!CanEdit || index < 0 || index >= _loginCompleteCommands.Count)
            return;

        _loginCompleteCommands.RemoveAt(index);
        _store!.SetOnLoginCompleteCommands(_scopePath, _loginCompleteCommands);
        AdjustSelectionAfterDelete(ref RefLoginCompleteSelectedRow(), index);
    }

    // ---- Periodic -----------------------------------------------------------

    public string PeriodicText { get; private set; } = string.Empty;
    public Action<string> SetPeriodicText { get; }
    public string PeriodicIntervalText { get; private set; } = "1";
    public Action<string> SetPeriodicInterval { get; }
    public string PeriodicOffsetText { get; private set; } = "0";
    public Action<string> SetPeriodicOffset { get; }
    public Action AddPeriodicCommand { get; }

    public IReadOnlyList<string> PeriodicCommands =>
        [.. _periodicCommands.Select(static command => command.Command)];

    public IReadOnlyList<string> PeriodicIntervals =>
        [.. _periodicCommands.Select(static command =>
            ((int)command.Interval.TotalMinutes)
                .ToString(System.Globalization.CultureInfo.InvariantCulture))];

    public IReadOnlyList<string> PeriodicOffsets =>
        [.. _periodicCommands.Select(static command =>
            ((int)command.OffsetFromMidnight.TotalMinutes)
                .ToString(System.Globalization.CultureInfo.InvariantCulture))];

    public IReadOnlyList<uint> PeriodicDeleteIcons => RepeatIcon(DeleteIconId, _periodicCommands.Count);
    public Action<int> DeletePeriodicCommand { get; }
    public int PeriodicSelectedRow { get => _periodicSelectedRowField; private set => _periodicSelectedRowField = value; }
    public Action<int> SelectPeriodicRow { get; }

    /// <summary>
    /// M2 (<c>AccountServerCharacterGUI.cs:257</c>): the original refuses the
    /// add outright -- no row, no store write, every typed field stays
    /// exactly as entered -- for an empty/unparseable interval or offset, an
    /// interval &lt;= 0, or a negative offset. It never falls back to a
    /// default the way a lenient parser would.
    /// </summary>
    /// <remarks>
    /// LOW-1: on a SUCCESSFUL add, only <see cref="PeriodicText"/> clears --
    /// <see cref="PeriodicIntervalText"/>/<see cref="PeriodicOffsetText"/>
    /// are left exactly as typed (not reset to "1"/"0"), matching the
    /// original: they are separate `Edit` controls the original never
    /// touched on Add, so a user adding several commands at the same
    /// interval/offset does not have to retype them each time. The command
    /// text itself is stored VERBATIM -- no trim -- matching the original,
    /// which read the Edit control's text straight into the new row with no
    /// whitespace cleanup; only an entirely empty string is refused.
    /// </remarks>
    private void AddPeriodic()
    {
        if (!CanEdit)
            return;
        string text = PeriodicText;
        if (text.Length == 0)
            return;
        if (!TryParseMinutes(PeriodicIntervalText, out int interval) || interval <= 0)
            return;
        if (!TryParseMinutes(PeriodicOffsetText, out int offset) || offset < 0)
            return;

        _periodicCommands.Add(new Settings.PeriodicCommand(
            text, TimeSpan.FromMinutes(interval), TimeSpan.FromMinutes(offset)));
        _store!.SetPeriodicCommands(_scopePath, _periodicCommands);

        PeriodicText = string.Empty;
    }

    private void DeletePeriodic(int index)
    {
        if (!CanEdit || index < 0 || index >= _periodicCommands.Count)
            return;

        _periodicCommands.RemoveAt(index);
        _store!.SetPeriodicCommands(_scopePath, _periodicCommands);
        AdjustSelectionAfterDelete(ref RefPeriodicSelectedRow(), index);
    }

    private static bool TryParseMinutes(string text, out int value)
        => int.TryParse(
            text,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);

    private static void AdjustSelectionAfterDelete(ref int selectedRow, int deletedIndex)
    {
        if (selectedRow == deletedIndex)
            selectedRow = -1;
        else if (selectedRow > deletedIndex)
            selectedRow--;
    }

    // ref-returning accessors so the three near-identical delete handlers can
    // share one selection-adjustment helper without duplicating its logic.
    private ref int RefLoginSelectedRow() => ref _loginSelectedRowField;
    private ref int RefLoginCompleteSelectedRow() => ref _loginCompleteSelectedRowField;
    private ref int RefPeriodicSelectedRow() => ref _periodicSelectedRowField;

    private int _loginSelectedRowField = -1;
    private int _loginCompleteSelectedRowField = -1;
    private int _periodicSelectedRowField = -1;
}
