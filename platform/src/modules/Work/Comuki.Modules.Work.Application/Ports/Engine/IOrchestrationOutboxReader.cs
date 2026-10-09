namespace Comuki.Modules.Work.Application.Ports.Engine;

/// <summary>
/// Generic projection of an engine outbox row that the Work side
/// sees — the Work module never imports <c>Comuki.Engine.Orchestration</c>
/// directly, so the EF entity <c>OutboxMessage</c> is named here as
/// a primitive record. The host closes the port over a real
/// <c>OrchestrationDbContext</c> read in a thin adapter; the
/// subscriber path consumes the projection and never touches EF.
/// </summary>
/// <param name="Id">Postgres <c>uuid</c> — UUIDv7 in practice; drives the per-type watermark.</param>
/// <param name="Type">Outbox type discriminator (e.g. <c>work.task.attempt-cancelled.v1</c>); matches the row's <c>type</c> column.</param>
/// <param name="Payload">JSON payload as a string; the subscriber's <c>JsonElement.Parse</c> + typed <c>JsonSerializer.Deserialize&lt;T&gt;</c> reifies it per type.</param>
public sealed record OrchestrationOutboxRow(Guid Id, string Type, string Payload);

/// <summary>
/// Per-batch outbox read seam. The Work-side outbox-poll dispatcher
/// depends on this port — its implementation lives in the host
/// (where the engine <c>OrchestrationDbContext</c> registration
/// sits), and closes over the same SQL
/// <c>id &gt; lastSeenId</c> filter + batch limit + highest-seen-id
/// ordering the original implementation carried.
/// </summary>
public interface IOrchestrationOutboxReader
{
    /// <summary>
    /// Polls the orchestration outbox for the supplied
    /// <paramref name="types"/>, filtered by
    /// <c>id &gt; lastSeenId</c>, and returns the rows
    /// (ordered by id ascending) plus the highest id observed.
    /// </summary>
    /// <param name="types">Outbox type discriminators to subscribe to (e.g. <c>["work.task.created.v1", "work.task.attempt-cancelled.v1"]</c>); the SQL filter is <c>type = ANY(@types)</c>.</param>
    /// <param name="lastSeenId">Watermark — only rows with <c>id &gt; lastSeenId</c> are returned (per-batch, scoped to a single dispatch loop; the dispatcher persists <see cref="OrchestrationOutboxBatch.HighestSeenId"/> on success).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The next batch envelope: number of rows, the highest id observed, and the rows themselves.</returns>
    public Task<OrchestrationOutboxBatch> PollAsync(
        IReadOnlyCollection<string> types,
        Guid lastSeenId,
        CancellationToken cancellationToken = default);
}

/// <summary>Per-batch read result envelope.</summary>
/// <param name="Read">Number of rows in this batch; drives logging + the dispatcher's per-batch metric tag.</param>
/// <param name="HighestSeenId">Highest <c>id</c> observed (the SQL-ordered tail; falls back to the caller's <c>lastSeenId</c> on empty batches).</param>
/// <param name="Rows">The batch's rows in id-ascending order; empty when <c>Read</c> is zero.</param>
public sealed record OrchestrationOutboxBatch(int Read, Guid HighestSeenId, IReadOnlyList<OrchestrationOutboxRow> Rows);
