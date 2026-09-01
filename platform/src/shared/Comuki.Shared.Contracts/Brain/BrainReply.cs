namespace Comuki.Shared.Contracts.Brain;

/// <summary>
/// Result of one brain invocation: the streamed progress fragments (already
/// materialized — the live token stream surfaces with the SignalR slice) and
/// the final payload. For <c>plan</c> invocations <paramref name="FinalJson"/>
/// is the plan JSON validated by <see cref="Plans.PlanValidator"/>.
/// </summary>
/// <param name="Chunks">Progress fragments in arrival order; may be empty.</param>
/// <param name="FinalJson">Final payload (plan JSON for <c>plan</c>, reply text for <c>chat</c>).</param>
public sealed record BrainReply(IReadOnlyList<string> Chunks, string FinalJson);
