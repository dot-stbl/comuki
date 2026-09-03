namespace Comuki.Shared.Contracts.Artifacts;

/// <summary>
/// One run's terminal snapshot — what the journal knows about the run when it locks.
/// </summary>
/// <param name="RunId">Run the snapshot is about (matches the journal entry).</param>
/// <param name="Status">Lower-case terminal status wire string (see ArtifactPackageTriggers).</param>
/// <param name="OriginWorkItemId">Work item that originated the run; null for runs without a work item.</param>
/// <param name="DetailJson">Worker result JSON (the journal entry payload); null if absent.</param>
/// <param name="OccurredAt">Journal stamp for the terminal transition (UTC).</param>
public sealed record RunTerminalSnapshot(
    Guid RunId,
    string Status,
    Guid? OriginWorkItemId,
    string? DetailJson,
    DateTimeOffset OccurredAt);
