namespace Comuki.TestFakeModel.Determinism;

/// <summary>
/// A clock that starts at a fixed epoch and advances by a fixed step each
/// call, counted with an interlocked counter so concurrent requests still
/// get a stable, reproducible sequence of instants — no wall-clock reads,
/// no flakiness from test timing.
/// </summary>
/// <remarks>Creates a clock starting at <paramref name="epoch"/>, advancing by <paramref name="step"/> per call.</remarks>
public sealed class FixedStepClock(DateTimeOffset epoch, TimeSpan step) : IClock
{
    private long callCount = -1;

    /// <inheritdoc />
    public DateTimeOffset UtcNow()
    {
        var callIndex = Interlocked.Increment(ref callCount);
        return epoch + step * callIndex;
    }
}
