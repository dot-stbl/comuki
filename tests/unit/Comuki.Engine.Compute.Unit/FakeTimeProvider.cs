namespace Comuki.Engine.Compute.Unit;

/// <summary>
/// Deterministic clock for expiry tests — the issuer reads time exclusively
/// through the injected <see cref="TimeProvider"/>.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan duration)
    {
        utcNow = utcNow.Add(duration);
    }

    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }
}
