using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Trackers.Shared;

namespace OpenAC.MagTools.Trackers.Player;

/// <summary>
/// Wires <see cref="PlayerTracker"/> to the host: <c>Created</c>/<c>Moved</c>
/// and <c>&lt;Server&gt;/&lt;Character&gt;.PlayerTracker.xml</c> persistence.
/// </summary>
public sealed class PlayerTrackerHost
{
    private static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(10);

    private readonly IPluginHost _host;
    private readonly PlayerTrackerSettings _settings;
    private Action<PluginObjectChange>? _onObjectChanged;
    private IDisposable? _saveRegistration;
    private string _storageKey = string.Empty;
    private bool _running;

    public PlayerTrackerHost(IPluginHost host, PlayerTrackerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(settings);
        _host = host;
        _settings = settings;
        Tracker = new PlayerTracker();
    }

    public PlayerTracker Tracker { get; }

    public void Start(TickScheduler scheduler, string server, string character)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (_running)
            return;

        _running = true;
        _storageKey = server + "/" + character + ".PlayerTracker.xml";

        if (_settings.Persistent.Value && _host.Storage.ReadText(_storageKey) is { } content
            && PlayerTrackerXml.TryImport(content, out List<TrackedPlayer> imported))
        {
            Tracker.ImportStats(imported);
        }

        _onObjectChanged = OnObjectChanged;
        _host.Events.ObjectChanged += _onObjectChanged;

        _saveRegistration = scheduler.Every(SaveInterval, Save);
    }

    public void Stop()
    {
        if (!_running)
            return;

        // Save() gates on _running, so it must run BEFORE that flips to
        // false, or the flush is silently skipped (H8).
        Save();
        _running = false;

        if (_onObjectChanged is not null)
            _host.Events.ObjectChanged -= _onObjectChanged;
        _onObjectChanged = null;

        _saveRegistration?.Dispose();
        _saveRegistration = null;

        Tracker.ClearStats();
    }

    /// <summary>Flushes to storage without stopping — the shared 10-minute persistence save.</summary>
    public void Save()
    {
        if (_running && _settings.Persistent.Value && _storageKey.Length > 0)
            _host.Storage.WriteText(_storageKey, PlayerTrackerXml.Export(Tracker.Items));
    }

    private void OnObjectChanged(PluginObjectChange change)
    {
        if (!_settings.Enabled.Value)
            return;
        if (change.Kind != PluginObjectChangeKind.Created && change.Kind != PluginObjectChangeKind.Moved)
            return;

        if (!_host.Automation.Objects.TryGet(change.ObjectId, out PluginWorldObject wo)
            || wo.ObjectClass != PluginObjectClass.Player)
            return;

        // The host's Position is already in global compass units; map back
        // to the original's full-cell-id + landblock-local-metres storage
        // shape (see HostPosition's remarks) so exported XML stays
        // numerically interchangeable with the original.
        LandblockLocalPosition local = HostPosition.ToLandblockLocal(wo.Position);

        Tracker.ProcessWorldObject(
            new PlayerObservation(
                change.ObjectId,
                wo.Name,
                local.CellId,
                local.X,
                local.Y,
                local.Z),
            _host.Automation.Character.Name);
    }
}
