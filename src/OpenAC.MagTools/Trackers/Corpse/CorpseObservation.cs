namespace OpenAC.MagTools.Trackers.Corpse;

/// <summary>
/// The subset of a <c>PluginWorldObject</c>/<c>PluginItemProperties</c> pair
/// <see cref="CorpseTracker"/> needs, so the tracker's decision logic can be
/// unit tested without a fake host. The caller (<c>CorpseTrackerHost</c>)
/// already filtered this to <c>ObjectClass == Corpse</c>.
/// </summary>
public readonly record struct CorpseObservation(
    uint Id,
    int LandBlock,
    double X,
    double Y,
    double Z,
    string Name,
    int Burden,
    bool HasIdData,
    string? FullDescription);
