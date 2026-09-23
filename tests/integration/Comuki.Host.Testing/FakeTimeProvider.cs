namespace Comuki.Host.Testing;

/// <summary>
/// Deterministic clock for expiry/lease tests — a store or reaper reads
/// time exclusively through the injected <see cref="TimeProvider"/>, so a
/// test advances it instead of sleeping for real. Consolidates what were
/// two identically-shaped, independently-duplicated copies (WS2 task 2.4):
/// <c>Comuki.Engine.Orchestration.Integration.Queue</c> used the
/// parameterless constructor plus <see cref="Advance"/> to move a lease's
/// clock forward; <c>Comuki.Modules.Identity.Integration.Stores</c> used
/// the explicit-<paramref name="initial"/> constructor and never mutated
/// it. Both shapes are the same type here.
/// </summary>
/// <param name="initial">The starting reading <see cref="GetUtcNow"/> returns until the next <see cref="Advance"/>.</param>
public sealed class FakeTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private DateTimeOffset utcNow = initial;

    /// <summary>Same fixed instant every duplicate independently picked — kept as the parameterless default so existing call sites don't need to name one.</summary>
    public FakeTimeProvider()
        : this(new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero))
    {
    }

    /// <summary>Moves the clock forward by <paramref name="duration"/> — the only way time passes for a consumer reading through <see cref="TimeProvider"/>.</summary>
    public void Advance(TimeSpan duration)
    {
        utcNow = utcNow.Add(duration);
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }
}
