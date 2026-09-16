namespace OpenAC.MagTools.Tests.Trackers.Equipment;

/// <summary>A settable clock for exact burn-rate/depletion assertions.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan span) => Now += span;
}
