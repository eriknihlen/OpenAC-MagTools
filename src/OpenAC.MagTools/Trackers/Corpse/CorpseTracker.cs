namespace OpenAC.MagTools.Trackers.Corpse;

/// <summary>
/// Ports <c>Trackers/Corpse/CorpseTracker.cs</c>'s decision logic (verbatim
/// rules, see the port design's §2.2 and docs/research/mag-tools's §2.2). The
/// host-facing wiring (world events, the 60 s maintenance timer, storage)
/// lives in <see cref="CorpseTrackerHost"/> so this class can be unit tested
/// directly.
/// </summary>
/// <remarks>
/// The Fellow/Permitted-corpse settings are read by the host (so `/mt opt`
/// still lists and toggles them) but this class has no branch for them — the
/// original's own <c>TrackFellowCorpses</c>/<c>TrackPermittedCorpses</c>
/// branches are empty <c>// fix</c> stubs that never actually add a corpse to
/// tracking. See docs/deviations.md.
/// </remarks>
public sealed class CorpseTracker
{
    private readonly List<TrackedCorpse> _items = [];
    private readonly Func<DateTime> _now;

    public CorpseTracker(Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.Now);
    }

    public event Action<TrackedCorpse>? ItemAdded;
    public event Action<TrackedCorpse>? ItemChanged;
    public event Action<TrackedCorpse>? ItemRemoved;

    public IReadOnlyList<TrackedCorpse> Items => _items;

    /// <summary>
    /// <c>ProcessWorldObject</c>. The caller only invokes this for an object
    /// whose <c>ObjectClass == Corpse</c>. <paramref name="requestId"/> is
    /// called for the burden-heuristic path when the corpse has no id data
    /// yet (mirrors <c>Actions.RequestId</c>).
    /// </summary>
    public void ProcessWorldObject(
        CorpseObservation wo,
        string myCharacterName,
        bool trackAllCorpses,
        Action<uint> requestId)
    {
        ArgumentNullException.ThrowIfNull(requestId);

        // Guid reuse: same id, different landblock or moved > 1 unit.
        int existingIndex = _items.FindIndex(item => item.Id == wo.Id);
        if (existingIndex >= 0)
        {
            TrackedCorpse existing = _items[existingIndex];
            bool reused = existing.LandBlock != wo.LandBlock
                || Math.Abs(existing.LocationX - wo.X) > 1
                || Math.Abs(existing.LocationY - wo.Y) > 1;
            if (reused)
            {
                _items.RemoveAt(existingIndex);
                ItemRemoved?.Invoke(existing);
                existingIndex = -1;
            }
        }

        // Already tracked (and not a reused guid) -> nothing to do.
        if (existingIndex >= 0)
            return;

        bool trackCorpse = false;

        if (wo.Name.Contains(myCharacterName, StringComparison.Ordinal))
            trackCorpse = true;

        if (trackAllCorpses)
            trackCorpse = true;

        // Kill heuristic: a heavy corpse (implies a real kill, not a
        // decoration) whose FullDescription names me as the killer.
        if (!trackCorpse && wo.Burden > 6000)
        {
            if (!wo.HasIdData)
            {
                requestId(wo.Id);
            }
            else if (wo.FullDescription is { } description
                && description.Contains(myCharacterName, StringComparison.Ordinal))
            {
                trackCorpse = true;
            }
        }

        if (!trackCorpse)
            return;

        var trackedItem = new TrackedCorpse(
            wo.Id, _now(), wo.LandBlock, wo.X, wo.Y, wo.Z, wo.Name);
        _items.Add(trackedItem);
        ItemAdded?.Invoke(trackedItem);
    }

    /// <summary><c>Current_ContainerOpened</c>.</summary>
    public void OnContainerOpened(uint objectId)
    {
        if (objectId == 0u)
            return;

        int index = _items.FindIndex(item => item.Id == objectId);
        if (index < 0)
            return;

        TrackedCorpse item = _items[index];
        if (item.Opened)
            return;

        item.Opened = true;
        ItemChanged?.Invoke(item);
    }

    /// <summary>
    /// <c>WorldFilter_ReleaseObject</c>'s decay-in-front-of-you branch. The
    /// caller has already checked <c>ObjectClass == Corpse</c> and
    /// distance &lt;= 10 m from the local player.
    /// </summary>
    public void OnReleasedNearby(uint objectId)
    {
        int index = _items.FindIndex(item => item.Id == objectId);
        if (index < 0)
            return;

        TrackedCorpse item = _items[index];
        _items.RemoveAt(index);
        ItemRemoved?.Invoke(item);
    }

    /// <summary><c>CorpsePassesActiveTrackingFilter</c>: 7 days for my own corpses, 2 hours otherwise.</summary>
    public bool PassesActiveTrackingFilter(TrackedCorpse item, string myCharacterName)
    {
        TimeSpan age = _now() - item.TimeStamp;
        return item.Description.Contains(myCharacterName, StringComparison.Ordinal)
            ? age < TimeSpan.FromDays(7)
            : age < TimeSpan.FromHours(2);
    }

    /// <summary><c>maintenanceTimer_Tick</c>: drops anything the retention filter no longer keeps.</summary>
    public void RunMaintenance(string myCharacterName)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (PassesActiveTrackingFilter(_items[i], myCharacterName))
                continue;

            TrackedCorpse item = _items[i];
            _items.RemoveAt(i);
            ItemRemoved?.Invoke(item);
        }
    }

    public void ClearStats()
    {
        foreach (TrackedCorpse item in _items)
            ItemRemoved?.Invoke(item);
        _items.Clear();
    }

    /// <summary><c>ImportStats</c>: retention-filtered, id-deduplicated merge.</summary>
    public void ImportStats(IEnumerable<TrackedCorpse> imported, string myCharacterName)
    {
        var added = new List<TrackedCorpse>();
        foreach (TrackedCorpse newItem in imported)
        {
            if (!PassesActiveTrackingFilter(newItem, myCharacterName))
                continue;
            if (_items.Any(item => item.Id == newItem.Id))
                continue;

            _items.Add(newItem);
            added.Add(newItem);
        }

        foreach (TrackedCorpse item in added)
            ItemAdded?.Invoke(item);
    }

    /// <summary><c>ExportStats</c>'s filtering (the retention filter is applied before writing).</summary>
    public IReadOnlyList<TrackedCorpse> ItemsForExport(string myCharacterName)
        => _items.Where(item => PassesActiveTrackingFilter(item, myCharacterName)).ToList();
}
