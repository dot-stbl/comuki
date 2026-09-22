namespace Comuki.Engine.Orchestration.Application.MergeQueue.BatchClaim;

/// <summary>
/// Application-layer command: claim a merge batch (Pending →
/// InProgress). No payload beyond the target id — the operator is
/// whoever picked up the first entry.
/// </summary>
/// <param name="BatchId">Target batch.</param>
public sealed record ClaimMergeBatchCommand(Guid BatchId);
