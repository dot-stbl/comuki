namespace Comuki.Host.Realtime.Models;

/// <summary>
/// Attention signal broadcast to the <c>project:{id}:attention</c> group on
/// attention-worthy transitions (work item or run entering a state a human
/// should glance at: running, failed, escalated, awaiting approval).
/// </summary>
/// <param name="RunId">The run the transition happened in.</param>
/// <param name="ProjectId">The project the run belongs to.</param>
/// <param name="WorkItemId">The work item that transitioned, when applicable.</param>
/// <param name="Status">The target status (PascalCase domain name, e.g. <c>Failed</c>).</param>
/// <param name="AttentionKind">Lowercase wire kind: running / failed / escalated / awaiting_approval.</param>
/// <param name="OccurredAtUnixMs">When the transition happened — UTC unix milliseconds.</param>
public sealed record AttentionView(
    Guid RunId,
    Guid ProjectId,
    Guid? WorkItemId,
    string Status,
    string AttentionKind,
    long OccurredAtUnixMs);
