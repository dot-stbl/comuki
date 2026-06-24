namespace Comuki.Platform.Routing.Options;

/// <summary>
/// Одно правило детекции исчерпания квоты. Срабатывает, если совпали все
/// заданные поля. Хотя бы одно из <see cref="StatusCode"/> /
/// <see cref="BodyContains"/> должно быть задано (проверяется в Installer).
/// </summary>
public sealed class ExhaustionRule
{
    /// <summary>HTTP-статус ответа апстрима (например, 429). null = любой.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Подстрока в теле ответа (например, "insufficient balance"),
    /// сравнение ordinal-ignore-case. null = тело не проверяется.</summary>
    public string? BodyContains { get; init; }
}
