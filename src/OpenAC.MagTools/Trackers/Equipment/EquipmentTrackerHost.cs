using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Trackers.Equipment;

/// <summary>
/// Lifecycle owner for §2.4's equipment/mana tracker: rebuilds the tracked
/// set from scratch on login, resyncs it against every subsequent
/// <see cref="IEvents.ObjectChanged"/>, and issues an id request for a newly
/// tracked item exactly like the original's <c>Actions.RequestId(id)</c>.
/// </summary>
/// <remarks>
/// The original distinguished <c>CreateObject</c> (add) from a
/// <c>ChangeObject</c> carrying a "StorageChange" flag (add-or-remove) from
/// <c>ReleaseObject</c> (remove) — three distinct wire-level signals. The host
/// contract here only distinguishes Created/Updated/IdentReceived/Moved/Released,
/// with no "did the equip slot change" flag, so this host resyncs the WHOLE
/// equipped set (a handful of items, at most) against
/// <c>Items.CaptureOwnedItems()</c> on every Created/Updated/Released
/// notification instead of diffing a single object. The observable outcome —
/// equip and unequip both update the tracked set promptly — matches; the
/// three-event dance itself does not. See docs/deviations.md.
/// </remarks>
public sealed class EquipmentTrackerHost
{
    private readonly IPluginHost _host;
    private readonly TimeProvider _timeProvider;
    private Action<PluginObjectChange>? _onObjectChanged;
    private bool _running;

    public EquipmentTrackerHost(IPluginHost host, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Tracker = new EquipmentTracker(_timeProvider);
    }

    public EquipmentTracker Tracker { get; }

    public void Start()
    {
        if (_running)
            return;

        _running = true;
        Resync(identifyNewItems: true);

        _onObjectChanged = OnObjectChanged;
        _host.Events.ObjectChanged += _onObjectChanged;
    }

    public void Stop()
    {
        if (!_running)
            return;

        _running = false;
        if (_onObjectChanged is not null)
            _host.Events.ObjectChanged -= _onObjectChanged;
        _onObjectChanged = null;

        Tracker.Clear();
    }

    private void OnObjectChanged(PluginObjectChange change)
    {
        if (change.Kind == PluginObjectChangeKind.IdentReceived)
        {
            foreach (EquipmentTrackedItem item in Tracker.Items)
            {
                if (item.ObjectId != change.ObjectId)
                    continue;
                if (_host.Automation.Items.TryCaptureProperties(
                        change.ObjectId, out PluginItemProperties properties))
                {
                    item.OnIdentReceived(properties, _timeProvider.GetUtcNow().UtcDateTime);
                }

                break;
            }
        }

        Resync(identifyNewItems: true);
    }

    private void Resync(bool identifyNewItems)
    {
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        IReadOnlyList<PluginInventoryItem> owned = _host.Automation.Items.CaptureOwnedItems();

        var equippedIds = new HashSet<uint>();
        foreach (PluginInventoryItem item in owned)
        {
            if (!item.IsEquipped)
                continue;

            equippedIds.Add(item.ObjectId);

            bool isNew = !TryFind(item.ObjectId, out _);
            EquipmentTrackedItem tracked = Tracker.GetOrAdd(item.ObjectId);

            _host.Automation.Objects.TryGet(item.ObjectId, out PluginWorldObject world);
            tracked.UpdateSnapshot(item, world, now);

            if (isNew && identifyNewItems)
                _host.Automation.Objects.Identify(item.ObjectId);
        }

        List<uint>? toRemove = null;
        foreach (EquipmentTrackedItem tracked in Tracker.Items)
        {
            if (!equippedIds.Contains(tracked.ObjectId))
                (toRemove ??= []).Add(tracked.ObjectId);
        }

        if (toRemove is not null)
        {
            foreach (uint objectId in toRemove)
                Tracker.Remove(objectId);
        }

        Tracker.RaiseChanged();
    }

    private bool TryFind(uint objectId, out EquipmentTrackedItem? item)
    {
        foreach (EquipmentTrackedItem candidate in Tracker.Items)
        {
            if (candidate.ObjectId != objectId)
                continue;
            item = candidate;
            return true;
        }

        item = null;
        return false;
    }
}
