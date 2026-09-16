namespace OpenAC.MagTools.Tests.Trackers.Combat;

/// <summary>A settable clock for exact DPS-window assertions.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// What <see cref="TimeProvider.GetLocalNow"/> resolves against. Defaults
    /// to UTC so every existing test that only sets <see cref="Now"/> keeps
    /// treating it as both the UTC and the local time (deterministic
    /// regardless of the machine running the test); a test that specifically
    /// needs a non-UTC local zone (e.g. the periodic-commands local-clock
    /// tests) sets this explicitly.
    /// </summary>
    public TimeZoneInfo LocalZone { get; set; } = TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => LocalZone;

    public void Advance(TimeSpan span) => Now += span;
}
