using System.ComponentModel.DataAnnotations;

namespace Comuki.Platform.Routing.Options;

/// <summary>
/// Конфигурация ротации ключей Z.AI в модельном прокси.
/// </summary>
public sealed class RotationOptions
{
    /// <summary>Путь к секции в IConfiguration.</summary>
    public const string SectionName = "Routing:Rotation";

    /// <summary>Пул физических ключей Z.AI. Перебираются по порядку.</summary>
    [Required]
    [MinLength(1)]
    public required string[] ApiKeys { get; init; }

    /// <summary>Реальный upstream-URL Z.AI (Anthropic-совместимый).</summary>
    [Required]
    [Url]
    public required string UpstreamUrl { get; init; }

    /// <summary>Cooldown по умолчанию, если в ответе нет заголовка Retry-After.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan DefaultCooldown { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Правила детекции исчерпания квоты (логическое ИЛИ по списку).</summary>
    [Required]
    [MinLength(1)]
    public required ExhaustionRule[] ExhaustionRules { get; init; }

    /// <summary>
    /// Максимальный размер тела запроса (в байтах), который хранится в памяти при буферизации
    /// для повтора попытки с другим ключом. При превышении этого порога буфер сбрасывается на диск.
    /// Значение по умолчанию — 1 МБ (1 048 576 байт).
    /// </summary>
    [Range(1, int.MaxValue)]
    public int RequestBufferThresholdBytes { get; init; } = 1_048_576;
}
