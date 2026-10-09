namespace Comuki.Modules.Work.Infrastructure.Persistence.Entities;

/// <summary>
/// One row per <c>WorkTask.AppendAttempt</c> call — the immutable
/// attempt ledger the WorkTask aggregate carries in memory is
/// reconstructed from the latest row on hydrate. The infrastructure
/// inserts a row on <c>AppendAttempt</c> and stamps the terminal
/// status on <c>CompleteAttempt</c> in the same transaction as the
/// <c>work_tasks</c> write.
/// </summary>
public sealed record WorkTaskAttemptEntity(
    Guid Id,
    Guid TaskId,
    int AttemptOrdinal,
    Guid RunId,
    string? TerminalStatus,
    DateTimeOffset StartedAt,
    DateTimeOffset? TerminalAt)
{
    public WorkTaskAttemptEntity() : this(
        Id: Guid.Empty,
        TaskId: Guid.Empty,
        AttemptOrdinal: 0,
        RunId: Guid.Empty,
        TerminalStatus: null,
        StartedAt: default,
        TerminalAt: null)
    {
    }
}
