namespace OpenAC.MagTools.Ui;

/// <summary>
/// Tools → Inventory: the two clipboard buttons, the regex search box and the
/// result list with the selected item's info below it.
/// </summary>
/// <remarks>TODO(P3): item info, the export and the live search land together.</remarks>
public sealed class InventoryToolsPageViewModel : ListPageViewModel
{
    public InventoryToolsPageViewModel()
    {
        ClipboardWornEquipment = static () => { };
        ClipboardInventoryInfo = static () => { };
        SetSearchText = text => SearchText = text;
    }

    public Action ClipboardWornEquipment { get; }
    public Action ClipboardInventoryInfo { get; }

    /// <summary>The original seeded the box with this and cleared it on use.</summary>
    public string SearchText { get; private set; } = "regex search string";

    public Action<string> SetSearchText { get; }

    public IReadOnlyList<uint> ResultIcons { get; } = [];
    public IReadOnlyList<string> ResultNames { get; } = [];

    public string ItemText => string.Empty;
}

/// <summary>
/// Tools → Tinkering: the salvage-identification helper.
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
/// Tools → Character and Tools → Server: the on-login, on-login-complete and
/// periodic command lists for one scope.
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
