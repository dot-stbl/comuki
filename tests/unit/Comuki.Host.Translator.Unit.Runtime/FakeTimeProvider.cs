namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Deterministic clock for watchdog / deadline-policy unit tests —
/// the production code reads time exclusively through the injected
/// <see cref="TimeProvider"/>, so a test advances it instead of
/// sleeping for real. Mirrors the shape of the shared testing-infra
/// <c>FakeTimeProvider</c> in <c>Comuki.Host.Testing.Clocks</c>
/// (mirrored here because this is a unit-test project, not
/// integration, and unit tests must run without a Testcontainers
/// runtime).
/// </summary>
/// <remarks>Constructs a clock pinned to the explicit starting instant.</remarks>
/// <param name="initial">The reading <see cref="GetUtcNow"/> returns until the next <see cref="Advance"/>.</param>
internal sealed class FakeTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private DateTimeOffset utcNow = initial;

    /// <summary>Constructs a clock pinned to a fixed starting instant.</summary>
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
