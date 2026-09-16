namespace OpenAC.MagTools.Trackers;

/// <summary>
/// Port of the original's <c>Trackers.SnapShot&lt;T&gt;</c> — one timestamped
/// value. Shared infrastructure: P4's <see cref="ValueSnapShotGroup"/> uses it
/// for combat DPS windows; a later slice (P5) reuses it for other
/// time-windowed trackers (mana, profit/loss).
/// </summary>
public sealed class SnapShot<T>(DateTime timeStamp, T value)
{
    public DateTime TimeStamp { get; } = timeStamp;

    public T Value { get; } = value;
}
