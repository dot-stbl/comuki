namespace Comuki.Shared.Contracts.Chat;

/// <summary>
/// What producing one message cost. Everything is optional — a user row
/// has no model and a journal row has no tokens — so a partial reading is
/// expressible without inventing zeroes.
/// </summary>
/// <param name="Model">Model id that produced the message (<c>glm-4.7</c>, …); null for rows no model wrote.</param>
/// <param name="TokensIn">Prompt tokens consumed.</param>
/// <param name="TokensOut">Completion tokens produced.</param>
/// <param name="CostMicros">Cost in USD micros (the unit the costs capability journals).</param>
/// <param name="LatencyMs">Wall-clock time to produce the message, in milliseconds.</param>
/// <param name="StopReason">Why generation stopped (<c>stop</c>, <c>length</c>, <c>tool_use</c>, …).</param>
public sealed record ChatMessageMeta(
    string? Model = null,
    int? TokensIn = null,
    int? TokensOut = null,
    long? CostMicros = null,
    long? LatencyMs = null,
    string? StopReason = null);
