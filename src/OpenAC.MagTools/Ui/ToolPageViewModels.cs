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
        if (inventoryItem.ContainerObjectId != _host.Automation.Character.ObjectId)
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
/// Tools -&gt; Tinkering: the salvage-identification helper.
/// </summary>
/// <remarks>TODO(P6): the tinkering helper and its auto-confirm land together.</remarks>
public sealed class TinkeringPageViewModel : ListPageViewModel
{
    public TinkeringPageViewModel()
    {
        AddSelectedItem = static () => { };
        Start = static () => { };
        Stop = static () => { };
        SelectMaterial = material => SelectedMaterial = material;
        SetMinimumPercent = text => MinimumPercentText = text;
        SetTargetTotalTinks = text => TargetTotalTinksText = text;
        DeleteRow = static _ => { };
    }

    public Action AddSelectedItem { get; }
    public Action Start { get; }
    public Action Stop { get; }

    /// <summary>The original's fixed material choice list.</summary>
    public IReadOnlyList<string> Materials { get; } =
        ["Brass", "Granite", "Green Garnet", "Iron", "Mahogany", "Steel", "Velvet"];

    public string SelectedMaterial { get; private set; } = "Brass";

    public Action<string> SelectMaterial { get; }

    public string MinimumPercentText { get; private set; } = "100";

    public Action<string> SetMinimumPercent { get; }

    public string TargetTotalTinksText { get; private set; } = string.Empty;

    public Action<string> SetTargetTotalTinks { get; }

    public IReadOnlyList<string> CurrentMarks { get; } = [];
    public IReadOnlyList<uint> Icons { get; } = [];
    public IReadOnlyList<string> Names { get; } = [];
    public IReadOnlyList<string> Work { get; } = [];
    public IReadOnlyList<string> Tinks { get; } = [];
    public IReadOnlyList<uint> DeleteIcons { get; } = [];

    public Action<int> DeleteRow { get; }
}

/// <summary>
/// Tools -&gt; Character and Tools -&gt; Server: the on-login, on-login-complete
/// and periodic command lists for one scope.
/// </summary>
/// <remarks>
/// TODO(P8): the command runner and the scope binding (which needs the live
/// account/server/character) land with the login-conveniences slice. The row
/// icons are the original's up/down/delete art.
/// </remarks>
public sealed class ScopedCommandsPageViewModel
{
    public const uint UpIconId = 0x060028FCu;
    public const uint DownIconId = 0x060028FDu;
    public const uint DeleteIconId = 0x060011F8u;

    public ScopedCommandsPageViewModel()
    {
        SetLoginText = text => LoginText = text;
        SetLoginCompleteText = text => LoginCompleteText = text;
        SetPeriodicText = text => PeriodicText = text;
        SetPeriodicInterval = text => PeriodicIntervalText = text;
        SetPeriodicOffset = text => PeriodicOffsetText = text;

        AddLoginCommand = static () => { };
        AddLoginCompleteCommand = static () => { };
        AddPeriodicCommand = static () => { };

        MoveLoginCommandUp = static _ => { };
        MoveLoginCommandDown = static _ => { };
        DeleteLoginCommand = static _ => { };
        MoveLoginCompleteCommandUp = static _ => { };
        MoveLoginCompleteCommandDown = static _ => { };
        DeleteLoginCompleteCommand = static _ => { };
        DeletePeriodicCommand = static _ => { };

        SelectLoginRow = index => LoginSelectedRow = index;
        SelectLoginCompleteRow = index => LoginCompleteSelectedRow = index;
        SelectPeriodicRow = index => PeriodicSelectedRow = index;
    }

    public string LoginText { get; private set; } = string.Empty;
    public Action<string> SetLoginText { get; }
    public Action AddLoginCommand { get; }
    public IReadOnlyList<string> LoginCommands { get; } = [];
    public IReadOnlyList<uint> LoginUpIcons { get; } = [];
    public IReadOnlyList<uint> LoginDownIcons { get; } = [];
    public IReadOnlyList<uint> LoginDeleteIcons { get; } = [];
    public Action<int> MoveLoginCommandUp { get; }
    public Action<int> MoveLoginCommandDown { get; }
    public Action<int> DeleteLoginCommand { get; }
    public int LoginSelectedRow { get; private set; } = -1;
    public Action<int> SelectLoginRow { get; }

    public string LoginCompleteText { get; private set; } = string.Empty;
    public Action<string> SetLoginCompleteText { get; }
    public Action AddLoginCompleteCommand { get; }
    public IReadOnlyList<string> LoginCompleteCommands { get; } = [];
    public IReadOnlyList<uint> LoginCompleteUpIcons { get; } = [];
    public IReadOnlyList<uint> LoginCompleteDownIcons { get; } = [];
    public IReadOnlyList<uint> LoginCompleteDeleteIcons { get; } = [];
    public Action<int> MoveLoginCompleteCommandUp { get; }
    public Action<int> MoveLoginCompleteCommandDown { get; }
    public Action<int> DeleteLoginCompleteCommand { get; }
    public int LoginCompleteSelectedRow { get; private set; } = -1;
    public Action<int> SelectLoginCompleteRow { get; }

    public string PeriodicText { get; private set; } = string.Empty;
    public Action<string> SetPeriodicText { get; }
    public string PeriodicIntervalText { get; private set; } = "1";
    public Action<string> SetPeriodicInterval { get; }
    public string PeriodicOffsetText { get; private set; } = "0";
    public Action<string> SetPeriodicOffset { get; }
    public Action AddPeriodicCommand { get; }
    public IReadOnlyList<string> PeriodicCommands { get; } = [];
    public IReadOnlyList<string> PeriodicIntervals { get; } = [];
    public IReadOnlyList<string> PeriodicOffsets { get; } = [];
    public IReadOnlyList<uint> PeriodicDeleteIcons { get; } = [];
    public Action<int> DeletePeriodicCommand { get; }
    public int PeriodicSelectedRow { get; private set; } = -1;
    public Action<int> SelectPeriodicRow { get; }
}
