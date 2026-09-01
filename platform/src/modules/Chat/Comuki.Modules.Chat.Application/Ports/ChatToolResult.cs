namespace Comuki.Modules.Chat.Application.Ports;

/// <summary>
/// Outcome of a chat tool invocation. Failures are data, not exceptions:
/// the graph turns them into an honest assistant reply instead of failing
/// the turn.
/// </summary>
/// <param name="Succeeded">Whether the tool did its work.</param>
/// <param name="ResultJson">Tool payload as JSON (run id, runs list, …).</param>
/// <param name="FailureCode">Stable machine code when <paramref name="Succeeded"/> is false.</param>
/// <param name="NotImplemented">True when the tool is a documented stub (surfaces as 501 semantics).</param>
public sealed record ChatToolResult(
    bool Succeeded,
    string ResultJson,
    string? FailureCode,
    bool NotImplemented);
