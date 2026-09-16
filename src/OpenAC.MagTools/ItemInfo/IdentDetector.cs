using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.ItemInfo;

/// <summary>
/// Ports <c>DetectItemsIdentifiedByUser</c>: the "I asked for this ID" path.
/// </summary>
/// <remarks>
/// The original keyed off a real Win32 left-click message
/// (<c>WM_LBUTTONDOWN</c>) plus Decal's <c>ItemSelected</c> event, firing
/// <c>RequestId</c> only when a selection followed a left click within one
/// second. The host contract has no raw click event — only
/// <see cref="ISelectionService.Changed"/>, which already only fires from a
/// deliberate selection action (a click or a select command). This port
/// treats every selection change as the click the original waited for and
/// requests an id immediately when <c>LeftClickIdent</c> is on, rather than
/// tracking a separate click timestamp. See <c>docs/deviations.md</c>.
/// </remarks>
public sealed class UserIdentDetector : IDisposable
{
    private static readonly PluginObjectClass[] ExcludedClasses =
    [
        PluginObjectClass.Corpse,
        PluginObjectClass.Door,
        PluginObjectClass.Foci,
        PluginObjectClass.Housing,
        PluginObjectClass.Lifestone,
        PluginObjectClass.Npc,
        PluginObjectClass.Portal,
        PluginObjectClass.Vendor,
    ];

    private static readonly TimeSpan SelectionRetention = TimeSpan.FromSeconds(10);

    private readonly IPluginHost _host;
    private readonly ItemInfoOnIdentSettings _settings;
    private readonly Dictionary<uint, DateTimeOffset> _selectedAt = new();
    private readonly Action<SelectionChangedEvent> _onSelectionChanged;
    private readonly Action<PluginObjectChange> _onObjectChanged;
    private bool _subscribed;

    public UserIdentDetector(IPluginHost host, ItemInfoOnIdentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(settings);
        _host = host;
        _settings = settings;
        _onSelectionChanged = OnSelectionChanged;
        _onObjectChanged = OnObjectChanged;
    }

    /// <summary>Raised once per identified item that passes the exclusion filter.</summary>
    public event Action<ItemIdentArgs>? ItemIdentified;

    public void Start()
    {
        if (_subscribed)
            return;
        _host.Selection.Changed += _onSelectionChanged;
        _host.Events.ObjectChanged += _onObjectChanged;
        _subscribed = true;
    }

    public void Stop()
    {
        if (!_subscribed)
            return;
        _host.Selection.Changed -= _onSelectionChanged;
        _host.Events.ObjectChanged -= _onObjectChanged;
        _subscribed = false;
        _selectedAt.Clear();
    }

    public void Dispose() => Stop();

    private void OnSelectionChanged(SelectionChangedEvent change)
    {
        if (!_settings.Enabled.Value)
            return;

        if (change.SelectedObjectId is not { } objectId || objectId == 0)
            return;

        _selectedAt[objectId] = DateTimeOffset.UtcNow;

        // Deviation: the original gated this on a real click within the last
        // second; here every selection change IS the click (see class remarks).
        if (_settings.LeftClickIdent.Value)
            _host.Automation.Objects.Identify(objectId);
    }

    private void OnObjectChanged(PluginObjectChange change)
    {
        if (!_settings.Enabled.Value)
            return;
        if (change.Kind != PluginObjectChangeKind.IdentReceived)
            return;

        PruneStaleSelections();

        if (!_selectedAt.Remove(change.ObjectId))
            return;

        if (!_host.Automation.Objects.TryGet(change.ObjectId, out PluginWorldObject worldObject))
            return;

        if (Array.IndexOf(ExcludedClasses, worldObject.ObjectClass) >= 0)
            return;

        ItemIdentified?.Invoke(new ItemIdentArgs(
            change.ObjectId,
            DontShowIfItemHasNoRule: false,
            DontShowIfIsSalvageRule: false,
            AllowAutoClipboard: true));
    }

    private void PruneStaleSelections()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<uint>? stale = null;
        foreach ((uint objectId, DateTimeOffset selectedAt) in _selectedAt)
        {
            if (now - selectedAt <= SelectionRetention)
                continue;
            stale ??= [];
            stale.Add(objectId);
        }

        if (stale is null)
            return;

        foreach (uint objectId in stale)
            _selectedAt.Remove(objectId);
    }
}

/// <summary>
/// Ports <c>DetectItemsInContainers</c>: the "container just opened" path.
/// Scans a freshly opened container's contents on a 100 ms think loop and
/// raises one ident event per not-yet-processed item, for at least 5 seconds.
/// </summary>
public sealed class ContainerIdentDetector : IDisposable
{
    private static readonly TimeSpan ThinkInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MinimumRunTime = TimeSpan.FromSeconds(5);

    private readonly IPluginHost _host;
    private readonly ItemInfoOnIdentSettings _settings;
    private readonly TickScheduler _scheduler;
    private readonly HashSet<uint> _itemsProcessed = [];
    private readonly Action<uint> _onContainerOpened;

    private IDisposable? _thinkLoop;
    private double _startedAtElapsed;
    private bool _subscribed;

    public ContainerIdentDetector(
        IPluginHost host,
        ItemInfoOnIdentSettings settings,
        TickScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(scheduler);
        _host = host;
        _settings = settings;
        _scheduler = scheduler;
        _onContainerOpened = OnContainerOpened;
    }

    /// <summary>Raised once per not-yet-processed item in the open container.</summary>
    public event Action<ItemIdentArgs>? ItemIdentified;

    /// <summary>True while the 100 ms think loop is actively scanning a container.</summary>
    public bool IsRunning => _thinkLoop is not null;

    public void Start()
    {
        if (_subscribed)
            return;
        _host.Events.ContainerOpened += _onContainerOpened;
        _subscribed = true;
    }

    public void Stop()
    {
        if (!_subscribed)
            return;
        _host.Events.ContainerOpened -= _onContainerOpened;
        _subscribed = false;
        StopThinking();
    }

    public void Dispose() => Stop();

    private void OnContainerOpened(uint containerObjectId)
    {
        if (!_settings.Enabled.Value)
            return;

        if (!_host.Automation.Objects.TryGet(containerObjectId, out PluginWorldObject container))
            return;

        // Never ident housing storage.
        if (container.Name == "Storage")
            return;

        StartThinking();
    }

    private void StartThinking()
    {
        if (_thinkLoop is not null)
            return;

        _startedAtElapsed = _scheduler.ElapsedSeconds;
        _thinkLoop = _scheduler.Every(ThinkInterval, Think);
    }

    private void StopThinking()
    {
        _thinkLoop?.Dispose();
        _thinkLoop = null;
    }

    /// <summary>
    /// <c>DontShowIfIsSalvageRule</c> is verbatim's own per-think recompute,
    /// not a value cached once at container-open time: the original
    /// re-resolved the currently open container's name from
    /// <c>CoreManager.Current.Actions.OpenedContainer</c> on every
    /// <c>Think()</c> call, so a container swap (open A, close, open B)
    /// without leaving this loop's 5-second tail picks up the new
    /// container's name instead of the one that started the scan.
    /// </summary>
    private bool CurrentDontShowIfIsSalvageRule(uint openContainerId)
    {
        if (!_host.Automation.Objects.TryGet(openContainerId, out PluginWorldObject container))
            return false;

        return container.Name.Contains("Chest", StringComparison.Ordinal)
            || container.Name.Contains("Vault", StringComparison.Ordinal)
            || container.Name.Contains("Reliquary", StringComparison.Ordinal);
    }

    private void Think()
    {
        uint openContainerId = _host.Automation.Objects.OpenContainerObjectId;
        if (openContainerId == 0)
        {
            StopThinking();
            return;
        }

        // Direct children of the open container only — the original's
        // GetByContainer(openedContainerId) never descended into nested
        // containers. The host's CaptureCurrentContents() walks the whole
        // container tree, so filter to items whose immediate container IS
        // the one that's open.
        IReadOnlyList<PluginInventoryItem> allContents = _host.Automation.Loot.CaptureCurrentContents();
        if (allContents.Count == 0)
            return; // sometimes it takes a bit for the contents to arrive

        bool dontShowIfIsSalvageRule = CurrentDontShowIfIsSalvageRule(openContainerId);

        foreach (PluginInventoryItem item in allContents)
        {
            if (item.ContainerObjectId != openContainerId)
                continue;
            if (!_itemsProcessed.Add(item.ObjectId))
                continue;

            ItemIdentified?.Invoke(new ItemIdentArgs(
                item.ObjectId,
                DontShowIfItemHasNoRule: true,
                DontShowIfIsSalvageRule: dontShowIfIsSalvageRule,
                AllowAutoClipboard: false));
        }

        // Some chests take a few seconds to print out ALL item info.
        if (_scheduler.ElapsedSeconds - _startedAtElapsed < MinimumRunTime.TotalSeconds)
            return;

        StopThinking();
    }
}
