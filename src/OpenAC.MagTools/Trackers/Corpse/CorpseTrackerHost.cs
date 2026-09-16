using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Trackers.Shared;

namespace OpenAC.MagTools.Trackers.Corpse;

/// <summary>
/// Wires <see cref="CorpseTracker"/> to the host: <c>Created</c>/<c>Updated</c>
/// (only when the update carries fresh ident data)/<c>Released</c>,
/// <c>ContainerOpened</c>, the 60 s maintenance timer, and
/// <c>&lt;Server&gt;/&lt;Character&gt;.CorpseTracker.xml</c> persistence.
/// </summary>
public sealed class CorpseTrackerHost
{
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(10);

    private readonly IPluginHost _host;
    private readonly CorpseTrackerSettings _settings;
    private Action<PluginObjectChange>? _onObjectChanged;
    private Action<uint>? _onContainerOpened;
    private IDisposable? _maintenanceRegistration;
    private IDisposable? _saveRegistration;
    private string _storageKey = string.Empty;
    private bool _running;

    public CorpseTrackerHost(IPluginHost host, CorpseTrackerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(settings);
        _host = host;
        _settings = settings;
        Tracker = new CorpseTracker();
    }

    public CorpseTracker Tracker { get; }

    public void Start(TickScheduler scheduler, string server, string character)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (_running)
            return;

        _running = true;
        _storageKey = server + "/" + character + ".CorpseTracker.xml";

        if (_settings.Persistent.Value && _host.Storage.ReadText(_storageKey) is { } content
            && CorpseTrackerXml.TryImport(content, out List<TrackedCorpse> imported))
        {
            Tracker.ImportStats(imported, MyName);
        }

        _onObjectChanged = OnObjectChanged;
        _host.Events.ObjectChanged += _onObjectChanged;

        _onContainerOpened = Tracker.OnContainerOpened;
        _host.Events.ContainerOpened += _onContainerOpened;

        _maintenanceRegistration = scheduler.Every(MaintenanceInterval, () => Tracker.RunMaintenance(MyName));
        _saveRegistration = scheduler.Every(SaveInterval, Save);
    }

    public void Stop()
    {
        if (!_running)
            return;
        _running = false;

        if (_settings.Persistent.Value && _storageKey.Length > 0)
            _host.Storage.WriteText(_storageKey, CorpseTrackerXml.Export(Tracker.ItemsForExport(MyName)));

        if (_onObjectChanged is not null)
            _host.Events.ObjectChanged -= _onObjectChanged;
        _onObjectChanged = null;

        if (_onContainerOpened is not null)
            _host.Events.ContainerOpened -= _onContainerOpened;
        _onContainerOpened = null;

        _maintenanceRegistration?.Dispose();
        _maintenanceRegistration = null;
        _saveRegistration?.Dispose();
        _saveRegistration = null;

        Tracker.ClearStats();
    }

    /// <summary>Flushes to storage without stopping — the shared 10-minute persistence save.</summary>
    public void Save()
    {
        if (_running && _settings.Persistent.Value && _storageKey.Length > 0)
            _host.Storage.WriteText(_storageKey, CorpseTrackerXml.Export(Tracker.ItemsForExport(MyName)));
    }

    private string MyName => _host.Automation.Character.Name;

    private void OnObjectChanged(PluginObjectChange change)
    {
        if (!_settings.Enabled.Value)
            return;

        switch (change.Kind)
        {
            case PluginObjectChangeKind.Created:
                ProcessIfCorpse(change.ObjectId);
                break;
            case PluginObjectChangeKind.IdentReceived:
                ProcessIfCorpse(change.ObjectId);
                break;
            case PluginObjectChangeKind.Released:
                OnReleased(change.ObjectId);
                break;
        }
    }

    private void ProcessIfCorpse(uint objectId)
    {
        if (!_host.Automation.Objects.TryGet(objectId, out PluginWorldObject wo)
            || wo.ObjectClass != PluginObjectClass.Corpse)
            return;

        if (!_host.Automation.Objects.TryCaptureProperties(objectId, out PluginItemProperties properties))
            properties = ItemModel.EmptyProperties;
        // Burden = PropertyInt.Burden (5). The original's Decal
        // StringValueKey.FullDescription is the host's PropertyString.LongDesc
        // (16) -- ACE renamed the same wire id; see docs/deviations.md.
        int burden = properties.Ints.TryGetValue(5u, out int burdenValue) ? burdenValue : 0;
        string? fullDescription = properties.Strings.TryGetValue(16u, out string? text) ? text : null;

        // The host's Position is already in global compass units (see
        // HostPosition's remarks); map back to the original's full-cell-id +
        // landblock-local-metres storage shape so the exported XML stays
        // numerically interchangeable with the original.
        LandblockLocalPosition local = HostPosition.ToLandblockLocal(wo.Position);

        var observation = new CorpseObservation(
            objectId,
            local.CellId,
            local.X,
            local.Y,
            local.Z,
            wo.Name,
            burden,
            wo.HasAppraisalData,
            fullDescription);

        Tracker.ProcessWorldObject(
            observation,
            MyName,
            _settings.TrackAllCorpses.Value,
            id => _host.Automation.Objects.Identify(id));
    }

    /// <summary>
    /// The released object has already left the object table, so distance is
    /// computed from the LOCAL PLAYER's live position to the TRACKED corpse's
    /// own stored coordinates. <see cref="TrackedCorpse.LandBlock"/> is the
    /// full cell id and LocationX/Y are landblock-local metres (see
    /// <see cref="HostPosition"/>'s remarks), so re-deriving compass units
    /// via <see cref="Coords.FromLandblock"/> reproduces exactly the same
    /// global units the live <see cref="PluginNavigationSnapshot.Position"/>
    /// is already in, making the two directly comparable via
    /// <see cref="PluginNavigationPosition.HorizontalDistanceMeters"/>.
    /// </summary>
    private void OnReleased(uint objectId)
    {
        TrackedCorpse? tracked = Tracker.Items.FirstOrDefault(item => item.Id == objectId);
        if (tracked is null)
            return;

        PluginNavigationSnapshot snapshot = _host.Automation.Navigation.Snapshot;
        if (!snapshot.IsAvailable)
            return;

        Coords corpseCoords = Coords.FromLandblock(tracked.LandBlock, tracked.LocationX, tracked.LocationY);
        var corpsePosition = new PluginNavigationPosition(
            0u, corpseCoords.Longitude, corpseCoords.Latitude, tracked.LocationZ / 240d, 0f, true);

        double distance = snapshot.Position.HorizontalDistanceMeters(corpsePosition);
        if (distance <= 10d)
            Tracker.OnReleasedNearby(objectId);
    }
}
