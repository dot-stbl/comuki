namespace Comuki.Engine.Orchestration.Integration.Queue;

/// <summary>
/// Deterministic clock — leases are handed out relative to it and the reaper
/// reads it, so expiry tests advance time instead of sleeping.
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset utcNow = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan duration)
    {
        utcNow = utcNow.Add(duration);
    }

    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }
}
