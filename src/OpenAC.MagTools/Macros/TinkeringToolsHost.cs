using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;

namespace OpenAC.MagTools.Macros;

/// <summary>One row of the Tinkering tab's list: icon/name/work/tinks + the hidden object id.</summary>
public sealed record TinkeringRow(uint ObjectId, uint IconId, string Name, string Work, string Tinks);

/// <summary>
/// Ports <c>Views/TinkeringToolsView.cs</c> (the port design's §4.1 Tinkering
/// tab, minus the auto-confirm half already covered by
/// <see cref="TinkeringAutoConfirm"/>): Add Selected Item (class filter,
/// dedup, request id, row fills Work/Tinks on IdentReceived), the fixed
/// material list, and Start/Stop's 1-second salvage-material scan.
/// </summary>
public sealed class TinkeringToolsHost
{
    /// <summary>The original's fixed material choice list, in its own declared order.</summary>
    public static readonly IReadOnlyList<string> Materials =
        ["Brass", "Granite", "Green Garnet", "Iron", "Mahogany", "Steel", "Velvet"];

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(1);

    private readonly IPluginHost _host;
    private readonly List<TinkeringRow> _rows = [];
    private Action<PluginObjectChange>? _onObjectChanged;
    private IDisposable? _scanRegistration;
    private TickScheduler? _scheduler;
    private Func<string> _materialProvider = () => Materials[0];
    private bool _attached;
    private bool _scanning;

    public TinkeringToolsHost(IPluginHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <summary>Raised whenever the row list changes (add/delete/ident-fill).</summary>
    public event Action? Changed;

    public IReadOnlyList<TinkeringRow> Rows => _rows;

    public bool IsScanning => _scanning;

    /// <summary><c>ObjectClassNeedsIdent</c>'s equivalent for Add Selected Item's class filter.</summary>
    public static bool CanAdd(PluginObjectClass objectClass)
        => objectClass is PluginObjectClass.Armor or PluginObjectClass.Clothing
            or PluginObjectClass.MeleeWeapon or PluginObjectClass.MissileWeapon
            or PluginObjectClass.WandStaffOrb or PluginObjectClass.Jewelry;

    /// <summary>Session-lifetime subscription for row Work/Tinks fills on IdentReceived. Independent of the scan.</summary>
    public void Attach(TickScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (_attached)
            return;
        _attached = true;
        _scheduler = scheduler;

        _onObjectChanged = OnObjectChanged;
        _host.Events.ObjectChanged += _onObjectChanged;
    }

    public void Detach()
    {
        if (!_attached)
            return;
        _attached = false;

        if (_onObjectChanged is not null)
            _host.Events.ObjectChanged -= _onObjectChanged;
        _onObjectChanged = null;

        StopScan();
        _scheduler = null;
        _rows.Clear();

        // M6: TinkeringPageViewModel caches its row projection until
        // Changed fires (P6 re-review item 2) -- without raising it here,
        // the cached tab keeps showing whatever rows were on screen right
        // before a logoff/Disable() instead of emptying with the session.
        Changed?.Invoke();
    }

    /// <summary><c>TinkeringAddSelectedItem_Hit</c>.</summary>
    public void AddSelectedItem()
    {
        uint? selected = _host.Selection.SelectedObjectId;
        if (selected is not { } objectId || objectId == 0u)
            return;
        if (!_host.Automation.Objects.TryGet(objectId, out PluginWorldObject wo))
            return;
        if (!CanAdd(wo.ObjectClass))
            return;
        if (_rows.Any(row => row.ObjectId == objectId))
            return;

        _rows.Add(new TinkeringRow(objectId, wo.IconId, wo.Name, string.Empty, string.Empty));
        _host.Automation.Objects.Identify(objectId);
        Changed?.Invoke();
    }

    /// <summary><c>TinkeringList_Click</c>'s delete column.</summary>
    public void DeleteRow(int index)
    {
        if (index < 0 || index >= _rows.Count)
            return;
        _rows.RemoveAt(index);
        Changed?.Invoke();
    }

    /// <summary><c>TinkeringList_Click</c>'s non-delete columns.</summary>
    public void SelectRow(int index)
    {
        if (index < 0 || index >= _rows.Count)
            return;
        _host.Selection.Select(_rows[index].ObjectId);
    }

    /// <summary>
    /// <c>TinkeringStart_Hit</c>: request every unidentified matching salvage
    /// bag once, then start the 1 s timer. <paramref name="materialProvider"/>
    /// is re-invoked on every tick (see <see cref="ScanTick"/>) rather than
    /// captured once, matching the original reading the dropdown's CURRENT
    /// selection every timer tick rather than snapshotting it at Start time
    /// -- a material change mid-scan takes effect on the very next tick.
    /// </summary>
    public void StartScan(Func<string> materialProvider)
    {
        ArgumentNullException.ThrowIfNull(materialProvider);
        if (_scanning || _scheduler is null)
            return;
        _scanning = true;
        _materialProvider = materialProvider;

        foreach (PluginInventoryItem item in _host.Automation.Items.CaptureOwnedItems())
        {
            if (!IsUnidentifiedMatchingSalvage(item))
                continue;
            _host.Automation.Objects.Identify(item.ObjectId);
        }

        _scanRegistration = _scheduler.Every(ScanInterval, ScanTick);
    }

    /// <summary><c>TinkeringStop_Hit</c>.</summary>
    public void StopScan()
    {
        if (!_scanning)
            return;
        _scanning = false;

        _scanRegistration?.Dispose();
        _scanRegistration = null;
    }

    /// <summary>
    /// <c>timer_Tick</c>: stop on leaving Peace mode, skip while busy,
    /// request ONE unidentified matching salvage bag's id per tick, and stop
    /// once none remain.
    /// </summary>
    private void ScanTick()
    {
        if (!_scanning)
            return;

        if (_host.Automation.Combat.Snapshot.Mode != PluginCombatMode.Peace)
        {
            StopScan();
            return;
        }

        if (_host.Automation.Items.IsBusy)
            return;

        foreach (PluginInventoryItem item in _host.Automation.Items.CaptureOwnedItems())
        {
            if (!IsUnidentifiedMatchingSalvage(item))
                continue;
            _host.Automation.Objects.Identify(item.ObjectId);
            return;
        }

        StopScan();
    }

    private bool IsUnidentifiedMatchingSalvage(PluginInventoryItem item)
    {
        if (item.ObjectClass != PluginObjectClass.Salvage)
            return false;
        if (!_host.Automation.Objects.TryGet(item.ObjectId, out PluginWorldObject wo) || wo.HasAppraisalData)
            return false;
        if (!_host.Automation.Objects.TryCaptureProperties(item.ObjectId, out PluginItemProperties properties))
            return false;

        int materialId = properties.Ints.TryGetValue((uint)ItemModel.MaterialKey, out int value) ? value : 0;
        return Dictionaries.MaterialInfo.TryGetValue(materialId, out string? name) && name == _materialProvider();
    }

    /// <summary><c>WorldFilter_ChangeObject</c>: fills Work/Tinks for a tracked row once its id arrives.</summary>
    private void OnObjectChanged(PluginObjectChange change)
    {
        if (change.Kind != PluginObjectChangeKind.IdentReceived)
            return;

        int index = _rows.FindIndex(row => row.ObjectId == change.ObjectId);
        if (index < 0)
            return;

        if (!_host.Automation.Objects.TryCaptureProperties(change.ObjectId, out PluginItemProperties properties))
            return;

        // -1, not 0, matches the original's MyWorldObject property-lookup
        // sentinel for "this int property is not present on the item" --
        // 0 is a real, distinct workmanship/tinker-count value.
        int workmanship = properties.Ints.TryGetValue((uint)ItemModel.WorkmanshipKey, out int work) ? work : -1;
        int tinks = properties.Ints.TryGetValue((uint)ItemModel.NumberTimesTinkeredKey, out int tinksValue) ? tinksValue : -1;

        TinkeringRow current = _rows[index];
        _rows[index] = current with
        {
            Work = workmanship.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Tinks = tinks.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        Changed?.Invoke();
    }
}
