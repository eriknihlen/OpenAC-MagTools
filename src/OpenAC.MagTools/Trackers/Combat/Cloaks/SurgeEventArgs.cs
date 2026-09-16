namespace OpenAC.MagTools.Trackers.Combat.Cloaks;

/// <summary>Port of the original's <c>Trackers.Combat.Cloaks.SurgeEventArgs</c>.</summary>
public sealed record SurgeEventArgs(string SourceName, string TargetName, SurgeType SurgeType);
