using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Intake.Domain.Sync;

/// <summary>
/// One sync-back unit of work: "tell the tracker the run behind this
/// ticket is <c>RunStatus</c> and link the run". Idempotent by the unique
/// index on <c>run_id</c> (a run reaches its single terminal status only
/// once); retries bump <see cref="Attempts"/> with exponential backoff on
/// <see cref="NextAttemptAt"/> until the attempt budget is exhausted.
/// </summary>
public sealed class SyncJob
{
    internal SyncJob()
    {
    }

    /// <summary>Job id (UUIDv7, client-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>The ticket whose run finished.</summary>
    public IncomingTicketId TicketId { get; private set; }

    /// <summary>The connection through which the tracker is reached.</summary>
    public SourceConnectionId ConnectionId { get; private set; }

    /// <summary>The finished run (no cross-context FK — stored as a plain uuid).</summary>
    public RunId RunId { get; private set; }

    /// <summary>Snapshot of the ticket's external id — survives ticket release.</summary>
    public string ExternalId { get; private set; } = string.Empty;

    /// <summary>Snapshot of the issue URL for the sync comment.</summary>
    public string ExternalUrl { get; private set; } = string.Empty;

    /// <summary>Terminal run status name (PascalCase enum name).</summary>
    public string RunStatus { get; private set; } = string.Empty;

    /// <summary>Job lifecycle status.</summary>
    public SyncJobStatus Status { get; private set; }

    /// <summary>Transition attempts so far.</summary>
    public int Attempts { get; private set; }

    /// <summary>Last failure reason, for manual inspection.</summary>
    public string? LastError { get; private set; }

    /// <summary>Earliest time the job may be attempted next.</summary>
    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>When the job was enqueued.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last mutation timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates a pending job, immediately due.</summary>
    /// <param name="ticketId"></param>
    /// <param name="connectionId"></param>
    /// <param name="runId"></param>
    /// <param name="externalId"></param>
    /// <param name="externalUrl"></param>
    /// <param name="runStatus"></param>
    /// <param name="now"></param>
    public static SyncJob Create(
        IncomingTicketId ticketId,
        SourceConnectionId connectionId,
        RunId runId,
        string externalId,
        string externalUrl,
        string runStatus,
        DateTimeOffset now)
    {
        return new SyncJob
        {
            Id = Guid.CreateVersion7(),
            TicketId = ticketId,
            ConnectionId = connectionId,
            RunId = runId,
            ExternalId = externalId,
            ExternalUrl = externalUrl,
            RunStatus = runStatus,
            Status = SyncJobStatus.Pending,
            Attempts = 0,
            LastError = null,
            NextAttemptAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Marks the job done — the tracker accepted the transition.</summary>
    /// <param name="now"></param>
    public void MarkDone(DateTimeOffset now)
    {
        Status = SyncJobStatus.Done;
        NextAttemptAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records a failed attempt: with budget left the job stays pending
    /// with the backoff applied; at the budget it is parked as failed.
    /// </summary>
    /// <param name="error"></param>
    /// <param name="maxAttempts"></param>
    /// <param name="backoff"></param>
    /// <param name="now"></param>
    public void MarkFailed(string error, int maxAttempts, TimeSpan backoff, DateTimeOffset now)
    {
        Attempts++;
        LastError = error;
        UpdatedAt = now;

        if (Attempts >= maxAttempts)
        {
            Status = SyncJobStatus.Failed;
            return;
        }

        // exponential backoff: base * 2^(attempts-1)
        NextAttemptAt = now + TimeSpan.FromTicks(backoff.Ticks * (1L << Math.Min(Attempts - 1, 20)));
    }
}
