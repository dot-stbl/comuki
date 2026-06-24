namespace Comuki.Platform.Routing.Interfaces;

/// <summary>
/// Решает, означает ли ответ апстрима исчерпание квоты/баланса ключа Z.AI.
/// Чистая функция — без состояния и без сети.
/// </summary>
public interface IQuotaExhaustionDetector
{
    /// <summary>
    /// true, если ответ совпал хотя бы с одним правилом исчерпания.
    /// </summary>
    /// <param name="statusCode">HTTP-статус ответа апстрима.</param>
    /// <param name="body">Тело ответа (для non-2xx; иначе null).</param>
    public bool IsExhausted(int statusCode, string? body);
}
