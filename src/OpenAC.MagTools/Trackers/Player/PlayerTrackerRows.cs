using System.Globalization;
using OpenAC.MagTools.Trackers.Shared;

namespace OpenAC.MagTools.Trackers.Player;

/// <summary>Columns 75/140/100 (Time <c>yy/MM/dd HH:mm</c>, Name, Coords) plus the hidden object id.</summary>
public sealed record PlayerRow(string Time, string Name, string Coords, uint ObjectId);

/// <summary>
/// Ports <c>PlayerTrackerGUI</c>'s row ordering: a new player is inserted at
/// the top immediately, but the FULL re-sort (newest-seen first, ties by
/// name) is rate-limited to once per 10 seconds — "some people report lag
/// while using Mag-Tools in populated areas".
/// </summary>
public sealed class PlayerTrackerRowsBuilder
{
    private static readonly TimeSpan SortInterval = TimeSpan.FromSeconds(10);

    private readonly Func<DateTime> _now;
    private readonly List<string> _order = [];
    private DateTime _lastSort = DateTime.MinValue;

    public PlayerTrackerRowsBuilder(Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.UtcNow);
    }

    public IReadOnlyList<PlayerRow> Build(PlayerTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        Dictionary<string, TrackedPlayer> byName = tracker.Items.ToDictionary(item => item.Name);

        // Drop names no longer tracked (Clear History or otherwise removed).
        _order.RemoveAll(name => !byName.ContainsKey(name));

        // New arrivals insert at the top, same as InsertRow(1).
        foreach (TrackedPlayer item in tracker.Items)
        {
            if (!_order.Contains(item.Name))
                _order.Insert(0, item.Name);
        }

        DateTime now = _now();
        if (now - _lastSort > SortInterval)
        {
            _order.Sort((a, b) =>
            {
                DateTime aSeen = byName[a].LastSeen;
                DateTime bSeen = byName[b].LastSeen;
                int byTime = bSeen.CompareTo(aSeen); // newest first
                return byTime != 0 ? byTime : string.CompareOrdinal(a, b);
            });
            _lastSort = now;
        }

        return _order
            .Select(name =>
            {
                TrackedPlayer item = byName[name];
                return new PlayerRow(
                    item.LastSeen.ToString("yy/MM/dd HH:mm", CultureInfo.InvariantCulture),
                    item.Name,
                    Coords.FromLandblock(item.LandBlock, item.LocationX, item.LocationY).ToString(),
                    item.Id);
            })
            .ToList();
    }
}
