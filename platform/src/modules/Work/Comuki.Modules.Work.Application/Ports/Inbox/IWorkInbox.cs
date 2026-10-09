namespace Comuki.Modules.Work.Application.Ports.Inbox;

/// <summary>
/// Mirror dedupe ledger for the Work inbox. The engine's durable
/// outbox publishes terminal events
/// (<c>orchestration.run.terminated.v1</c> /
/// <c>orchestration.run.cancelled.v1</c>) under a per-attempt key
/// that the Work side wants to claim. The dedupe key shape is
/// <c>work.task.{taskId}:dispatch:{ordinal}</c> for dispatch and
/// <c>work.task.{taskId}:terminal:{ordinal}</c> for terminal —
/// see <see cref="Shared.Contracts.Work.WorkDispatchItem"/>
/// for the helper. The implementation lives in
/// Work.Infrastructure; the Work.Application layer sees only
/// this port.
/// </summary>
public interface IWorkInbox
{
    /// <summary>
    /// Attempts to claim <paramref name="messageId"/> as newly
    /// seen. Returns <c>true</c> the first time a given id is
    /// claimed (caller proceeds); <c>false</c> on every later call
    /// with the same id — including a concurrent double-claim,
    /// which the underlying PK uniqueness constraint resolves to
    /// exactly one winner (caller treats <c>false</c> as
    /// "duplicate delivery, skip"). The implementation table is
    /// <c>inbox_receipts</c> in the <c>work</c> schema (task 3.2 of
    /// the change) — the actual table lands in Work.Infrastructure;
    /// the Work.Application port is what the handlers see.
    /// </summary>
    public Task<bool> TryClaimAsync(string messageId, CancellationToken cancellationToken = default);
}
