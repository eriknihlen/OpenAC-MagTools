using OpenAC.MagTools.Trackers.Combat.Cloaks;

namespace OpenAC.MagTools.Trackers.Combat;

/// <summary>Port of the original's <c>Trackers.Combat.CloakInfo</c>.</summary>
public sealed class CloakInfo(string sourceName, string targetName)
{
    public string SourceName { get; } = sourceName;

    public string TargetName { get; } = targetName;

    public int TotalSurges { get; set; }

    public void AddFromSurgeEventArgs(SurgeEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        TotalSurges++;
    }
}
