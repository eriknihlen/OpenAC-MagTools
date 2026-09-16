namespace OpenAC.MagTools.Trackers.Combat.Aetheria;

/// <summary>Port of the original's <c>Trackers.Combat.Aetheria.SurgeEventArgs</c>.</summary>
public sealed record SurgeEventArgs(string SourceName, string TargetName, SurgeType SurgeType);
