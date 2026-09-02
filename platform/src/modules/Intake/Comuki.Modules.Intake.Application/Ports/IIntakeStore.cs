using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Deliveries;
using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Modules.Intake.Domain.Rules;
using Comuki.Modules.Intake.Domain.Sync;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Intake.Application.Ports;

/// <summary>
/// The intake persistence port — everything the application layer needs
/// from the <c>IntakeDbContext</c>. Both idempotency locks surface as
/// boolean/nullable returns instead of exceptions: the unique indexes
/// are the arbiter, the store translates 23505 into the friendly shape.
/// </summary>
public interface IIntakeStore
{
    /// <summary>Connection lookup by webhook routing key; disabled connections read as absent.</summary>
    /// <param name="sourceKey"></param>
    /// <param name="webhookKey"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<SourceConnection?> FindConnectionByWebhookAsync(string sourceKey, string webhookKey, CancellationToken cancellationToken = default);

    /// <summary>Connection lookup by id.</summary>
    /// <param name="connectionId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<SourceConnection?> FindConnectionAsync(SourceConnectionId connectionId, CancellationToken cancellationToken = default);

    /// <summary>Lists the connections of one project (or all, when <paramref name="projectId"/> is null).</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IReadOnlyList<SourceConnection>> ListConnectionsAsync(ProjectId? projectId, CancellationToken cancellationToken = default);

    /// <summary>Inserts a new connection.</summary>
    /// <param name="connection"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task AddConnectionAsync(SourceConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Persists a mutated connection (the service loads, mutates, saves).</summary>
    /// <param name="connection"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task UpdateConnectionAsync(SourceConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Deletes a connection; missing ids are a no-op.</summary>
    /// <param name="connectionId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task DeleteConnectionAsync(SourceConnectionId connectionId, CancellationToken cancellationToken = default);

    /// <summary>Enabled admission rules of a project, oldest first (the first match decides).</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IReadOnlyList<AdmissionRule>> ListEnabledRulesAsync(ProjectId projectId, CancellationToken cancellationToken = default);

    /// <summary>Lists the rules of one project (or all), including disabled ones.</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IReadOnlyList<AdmissionRule>> ListRulesAsync(ProjectId? projectId, CancellationToken cancellationToken = default);

    /// <summary>Rule lookup by id.</summary>
    /// <param name="ruleId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<AdmissionRule?> FindRuleAsync(AdmissionRuleId ruleId, CancellationToken cancellationToken = default);

    /// <summary>Inserts a new rule.</summary>
    /// <param name="rule"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task AddRuleAsync(AdmissionRule rule, CancellationToken cancellationToken = default);

    /// <summary>Persists a mutated rule (the service loads, mutates, saves).</summary>
    /// <param name="rule"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task UpdateRuleAsync(AdmissionRule rule, CancellationToken cancellationToken = default);

    /// <summary>Deletes a rule; missing ids are a no-op.</summary>
    /// <param name="ruleId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task DeleteRuleAsync(AdmissionRuleId ruleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert-first delivery lock: the unique index on
    /// <c>(source, delivery_id)</c> rejects a repeat — false means replay.
    /// </summary>
    /// <param name="delivery"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<bool> TryInsertDeliveryAsync(IntakeDelivery delivery, CancellationToken cancellationToken = default);

    /// <summary>Records the pipeline outcome on a delivery row.</summary>
    /// <param name="deliveryId"></param>
    /// <param name="outcome"></param>
    /// <param name="detail"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task MarkDeliveryOutcomeAsync(Guid deliveryId, string outcome, string? detail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts an admitted ticket. Null means the one-live-run lock
    /// rejected it: an active ticket for the same
    /// <c>(project, provider, external_id)</c> already exists.
    /// </summary>
    /// <param name="ticket"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IncomingTicket?> TryInsertTicketAsync(IncomingTicket ticket, CancellationToken cancellationToken = default);

    /// <summary>Inserts a filtered-out ticket in <see cref="IntakeTicketStatus.Dismissed"/> — never lock-conflicting.</summary>
    /// <param name="ticket"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task AddDismissedTicketAsync(IncomingTicket ticket, CancellationToken cancellationToken = default);

    /// <summary>Ticket lookup by id.</summary>
    /// <param name="ticketId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IncomingTicket?> FindTicketAsync(IncomingTicketId ticketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Guarded claim: moves <c>Pending → Claimed</c> and stamps the run
    /// id. False when the ticket was claimed concurrently (or is not
    /// pending) — the guard runs in the database, races are safe.
    /// </summary>
    /// <param name="ticketId"></param>
    /// <param name="runId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<bool> TryMarkClaimedAsync(IncomingTicketId ticketId, RunId runId, CancellationToken cancellationToken = default);

    /// <summary>Pending tickets (the inbox), newest first.</summary>
    /// <param name="projectId">Optional project filter.</param>
    /// <param name="limit"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IReadOnlyList<IncomingTicket>> ListPendingAsync(ProjectId? projectId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Claimed tickets with a run id — the bridge's scan set, oldest first.</summary>
    /// <param name="limit"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IReadOnlyList<IncomingTicket>> ListClaimedAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Releases the ticket's lock (<c>Claimed → Done</c>) after its run finished.</summary>
    /// <param name="ticketId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task ReleaseTicketAsync(IncomingTicketId ticketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a sync job. The unique index on <c>run_id</c> makes the
    /// enqueue idempotent — a run reaches its terminal status only once.
    /// </summary>
    /// <param name="job"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task EnqueueSyncJobAsync(SyncJob job, CancellationToken cancellationToken = default);

    /// <summary>Due pending jobs (<c>next_attempt_at &lt;= now</c>), oldest first.</summary>
    /// <param name="now"></param>
    /// <param name="limit"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IReadOnlyList<SyncJob>> ListDueSyncJobsAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default);

    /// <summary>Marks the job done.</summary>
    /// <param name="jobId"></param>
    /// <param name="now"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task MarkSyncJobDoneAsync(Guid jobId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Marks the job failed and persists the backoff / parked state.</summary>
    /// <param name="jobId"></param>
    /// <param name="error"></param>
    /// <param name="maxAttempts"></param>
    /// <param name="backoff"></param>
    /// <param name="now"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task MarkSyncJobFailedAsync(Guid jobId, string error, int maxAttempts, TimeSpan backoff, DateTimeOffset now, CancellationToken cancellationToken = default);
}
