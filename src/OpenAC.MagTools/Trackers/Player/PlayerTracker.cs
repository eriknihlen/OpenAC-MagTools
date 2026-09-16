namespace OpenAC.MagTools.Trackers.Player;

/// <summary>
/// Ports <c>Trackers/Player/PlayerTracker.cs</c>'s decision logic verbatim:
/// keyed by name (not guid), never yourself, no expiry (entries only vanish
/// on Clear History). The host-facing wiring lives in
/// <see cref="PlayerTrackerHost"/>.
/// </summary>
public sealed class PlayerTracker
{
    private readonly List<TrackedPlayer> _items = [];
    private readonly Func<DateTime> _now;

    public PlayerTracker(Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.Now);
    }

    public event Action<TrackedPlayer>? ItemAdded;
    public event Action<TrackedPlayer>? ItemChanged;
    public event Action<TrackedPlayer>? ItemRemoved;

    public IReadOnlyList<TrackedPlayer> Items => _items;

    /// <summary><c>ProcessWorldObject</c>, shared by CreateObject and MoveObject.</summary>
    public void ProcessWorldObject(PlayerObservation wo, string myCharacterName)
    {
        if (string.Equals(wo.Name, myCharacterName, StringComparison.Ordinal))
            return;

        int index = _items.FindIndex(item => item.Name == wo.Name);
        if (index >= 0)
        {
            TrackedPlayer existing = _items[index];
            existing.LastSeen = _now();
            existing.LandBlock = wo.LandBlock;
            existing.LocationX = wo.X;
            existing.LocationY = wo.Y;
            existing.LocationZ = wo.Z;
            existing.Id = wo.Id;
            ItemChanged?.Invoke(existing);
            return;
        }

        var newItem = new TrackedPlayer(wo.Name, _now(), wo.LandBlock, wo.X, wo.Y, wo.Z, wo.Id);
        _items.Add(newItem);
        ItemAdded?.Invoke(newItem);
    }

    public void ClearStats()
    {
        foreach (TrackedPlayer item in _items)
            ItemRemoved?.Invoke(item);
        _items.Clear();
    }

    /// <summary><c>ImportStats</c>: deduplicated by name.</summary>
    public void ImportStats(IEnumerable<TrackedPlayer> imported)
    {
        var added = new List<TrackedPlayer>();
        foreach (TrackedPlayer newItem in imported)
        {
            if (_items.Any(item => item.Name == newItem.Name))
                continue;

            _items.Add(newItem);
            added.Add(newItem);
        }

        foreach (TrackedPlayer item in added)
            ItemAdded?.Invoke(item);
    }
}
