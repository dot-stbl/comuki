namespace Comuki.Platform.Routing.Unit.KeyRotation;

/// <summary>Минимальный управляемый TimeProvider для тестов (без внешних пакетов).</summary>
public sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan delta) => now += delta;
}
