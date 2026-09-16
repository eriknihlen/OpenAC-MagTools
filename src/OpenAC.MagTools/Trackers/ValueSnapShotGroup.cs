namespace OpenAC.MagTools.Trackers;

/// <summary>
/// Port of the original's <c>Trackers.ValueSnapShotGroup</c> — an
/// int-valued <see cref="SnapShotGroup{T}"/> whose <see cref="AddSnapShot"/>
/// SUMS into an existing same-timestamp slot instead of overwriting it (the
/// base class's plain <see cref="SnapShotGroup{T}.AddSnapShot"/> overwrites;
/// this hides that overload for combat-style "add this much more damage to
/// the current second" accumulation).
/// </summary>
public sealed class ValueSnapShotGroup(int minutesToRetain, TimeProvider? timeProvider = null)
    : SnapShotGroup<int>(minutesToRetain, timeProvider)
{
    /// <summary>
    /// For pruning to work properly and efficiently, it is assumed that you
    /// are adding items in order of oldest to newest. Use a
    /// <paramref name="minutesToRetain"/> greater than 0 to trim older
    /// SnapShots.
    /// </summary>
    public void AddSnapShot(DateTime timeStamp, int value, int minutesToRetain = 0)
    {
        for (int i = 0; i < SnapShots.Count; i++)
        {
            if (SnapShots[i].TimeStamp == timeStamp)
            {
                SnapShots[i] = new SnapShot<int>(timeStamp, SnapShots[i].Value + value);
                return;
            }
        }

        SnapShots.Add(new SnapShot<int>(timeStamp, value));

        Prune(minutesToRetain);
    }

    public int LastKnownValue => SnapShots.Count > 0 ? SnapShots[^1].Value : 0;

    /// <summary>
    /// How many snapshots are currently recorded. Lets a caller distinguish
    /// "never stamped a single snapshot yet" from "already at this same
    /// value" — <see cref="LastKnownValue"/> alone reads 0 for both.
    /// </summary>
    public int SnapShotCount => SnapShots.Count;

    /// <summary>Returns the value difference over the history of period.</summary>
    public double GetValueDifference(TimeSpan historyPeriod, TimeSpan usagePeriod)
    {
        if (SnapShots.Count == 1)
            return 0;

        SnapShot<int>? closestPastTarget = GetSnapShotClosestToTime(UtcNow - historyPeriod);
        if (closestPastTarget is null)
            return 0;

        return (LastKnownValue - closestPastTarget.Value)
            * (usagePeriod.TotalMinutes / (UtcNow - closestPastTarget.TimeStamp).TotalMinutes);
    }

    /// <summary>Returns the value total over the history of period.</summary>
    public int GetValueTotal(TimeSpan historyPeriod, out TimeSpan actualHistoryPeriodUsed)
    {
        actualHistoryPeriodUsed = TimeSpan.Zero;

        int total = 0;

        for (int i = SnapShots.Count - 1; i >= 0; i--)
        {
            if (UtcNow - SnapShots[i].TimeStamp > historyPeriod)
                break;

            total += SnapShots[i].Value;
            actualHistoryPeriodUsed = UtcNow - SnapShots[i].TimeStamp;
        }

        return total;
    }

    /// <summary>
    /// Returns the estimated time to depletion (Value of 0) given the
    /// history recorded over period.
    /// </summary>
    public TimeSpan GetTimeToDepletion(TimeSpan period)
    {
        if (SnapShots.Count == 1 || LastKnownValue == 0)
            return TimeSpan.Zero;

        SnapShot<int>? closestPastTarget = GetSnapShotClosestToTime(UtcNow - period);
        if (closestPastTarget is null)
            return TimeSpan.Zero;

        if (LastKnownValue >= closestPastTarget.Value)
            return TimeSpan.MaxValue;

        return TimeSpan.FromSeconds(
            LastKnownValue
            / ((closestPastTarget.Value - LastKnownValue)
                / (UtcNow - closestPastTarget.TimeStamp).TotalSeconds));
    }
}
