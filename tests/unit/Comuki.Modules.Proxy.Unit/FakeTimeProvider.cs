namespace Comuki.Modules.Proxy.Unit;

/// <summary>
/// Deterministic clock for expiry / budget tests — the resolver and
/// budget enforcer read time exclusively through the injected
/// <see cref="TimeProvider"/>.
/// </summary>
/// <remarks>Sets the initial clock reading.</remarks>
/// <param name="initial"></param>
internal sealed class FakeTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private readonly DateTimeOffset utcNow = initial;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }
}
