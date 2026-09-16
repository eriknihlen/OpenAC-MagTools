namespace OpenAC.MagTools.Trackers.Player;

/// <summary>
/// The subset of a <c>PluginWorldObject</c> <see cref="PlayerTracker"/> needs.
/// The caller (<c>PlayerTrackerHost</c>) already filtered this to
/// <c>ObjectClass == Player</c>.
/// </summary>
public readonly record struct PlayerObservation(
    uint Id,
    string Name,
    int LandBlock,
    double X,
    double Y,
    double Z);
