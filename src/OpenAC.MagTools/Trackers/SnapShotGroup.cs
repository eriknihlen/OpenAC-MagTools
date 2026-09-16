namespace OpenAC.MagTools.Trackers;

/// <summary>
/// Port of the original's <c>Trackers.SnapShotGroup&lt;T&gt;</c> — a
/// time-ordered, optionally-pruned list of <see cref="SnapShot{T}"/>.
/// </summary>
/// <remarks>
/// The original hardcoded <see cref="DateTime.UtcNow"/> everywhere it needed
/// "now"; this port takes a <see cref="TimeProvider"/> instead (default
/// <see cref="TimeProvider.System"/>) so a test can inject a fake clock and
/// assert exact DPS-window numbers instead of racing the wall clock. This is
/// a deliberate deviation — see docs/deviations.md.
/// </remarks>
public abstract class SnapShotGroup<T>
{
    protected readonly List<SnapShot<T>> SnapShots = [];

    private readonly int _minutesToRetain;
    private readonly TimeProvider _timeProvider;

    protected SnapShotGroup(int minutesToRetain, TimeProvider? timeProvider = null)
    {
        _minutesToRetain = minutesToRetain;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected TimeProvider TimeProvider => _timeProvider;

    protected DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>
    /// For pruning to work properly and efficiently, it is assumed that you
    /// are adding items in order of oldest to newest.
    /// </summary>
    public void AddSnapShot(DateTime timeStamp, T value)
    {
        for (int i = 0; i < SnapShots.Count; i++)
        {
            if (SnapShots[i].TimeStamp == timeStamp)
            {
                SnapShots[i] = new SnapShot<T>(timeStamp, value);
                return;
            }
        }

        SnapShots.Add(new SnapShot<T>(timeStamp, value));

        Prune(_minutesToRetain);
    }

    protected void Prune(int minutesToRetain)
    {
        if (minutesToRetain <= 0)
            return;

        for (int i = 0; i < SnapShots.Count; i++)
        {
            if (UtcNow - SnapShots[i].TimeStamp <= TimeSpan.FromMinutes(minutesToRetain))
                break;

            SnapShots.RemoveAt(i);
            i--;
        }
    }

    /// <summary>
    /// Gets the SnapShot closest to the time. Can return null if no
    /// SnapShots are present, or the only one present is <paramref name="excludeSnapShot"/>.
    /// </summary>
    protected SnapShot<T>? GetSnapShotClosestToTime(DateTime time, SnapShot<T>? excludeSnapShot = null)
    {
        SnapShot<T>? closestPastTarget = null;

        for (int i = SnapShots.Count - 1; i >= 0; i--)
        {
            if (excludeSnapShot is not null && ReferenceEquals(SnapShots[i], excludeSnapShot))
                continue;

            if (closestPastTarget is null
                || Math.Abs((time - SnapShots[i].TimeStamp).TotalMinutes)
                    < Math.Abs((time - closestPastTarget.TimeStamp).TotalMinutes))
            {
                closestPastTarget = SnapShots[i];
            }
        }

        return closestPastTarget;
    }
}
