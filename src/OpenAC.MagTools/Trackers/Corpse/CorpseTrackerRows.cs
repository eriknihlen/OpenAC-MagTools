using System.Globalization;
using OpenAC.MagTools.Trackers.Shared;

namespace OpenAC.MagTools.Trackers.Corpse;

/// <summary>
/// Ports <c>CorpseTrackerGUI</c>'s list projection: columns 53/162/100 (Time
/// <c>ddd HH:mm</c>, Name, Coords), newest inserted first, with the row's
/// hidden object id for <c>Selection.Select</c> on click. An opened corpse is
/// simply excluded from this snapshot — the original removed its row the
/// instant <c>Opened</c> flipped.
/// </summary>
public sealed record CorpseRow(string Time, string Name, string Coords, uint ObjectId);

public static class CorpseTrackerRows
{
    public static IReadOnlyList<CorpseRow> Build(CorpseTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        // Newest first, matching InsertRow(1) always inserting above row 1.
        return tracker.Items
            .Where(item => !item.Opened)
            .OrderByDescending(item => item.TimeStamp)
            .Select(item => new CorpseRow(
                item.TimeStamp.ToString("ddd HH:mm", CultureInfo.InvariantCulture),
                item.Description,
                Coords.FromLandblock(item.LandBlock, item.LocationX, item.LocationY).ToString(),
                item.Id))
            .ToList();
    }
}
