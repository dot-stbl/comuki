namespace Comuki.Platform.Routing.Interfaces;

/// <summary>
/// Пул физических ключей Z.AI с cooldown. Потокобезопасен: несколько
/// воркеров бьют в прокси параллельно. Состояние — в памяти инстанса.
/// </summary>
public interface IKeyPool
{
    /// <summary>Размер пула (общее число ключей, включая в cooldown).</summary>
    public int Count { get; }

    /// <summary>Первый ключ не в cooldown, либо null если все в cooldown.</summary>
    public string? TryAcquire();

    /// <summary>
    /// Пометить ключ исчерпанным: cooldown до now + retryAfter,
    /// либо до now + DefaultCooldown, если retryAfter не задан. Идемпотентно.
    /// </summary>
    public void MarkExhausted(string apiKey, TimeSpan? retryAfter);
}
