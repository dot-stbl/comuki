namespace Comuki.Engine.Orchestration.Infrastructure.OutboxDispatch;

/// <summary>
/// Delivers one outbox message to its transport. A thrown exception
/// counts as a delivery failure and feeds
/// <see cref="Domain.Outbox.OutboxMessage.RecordFailure"/>'s bounded
/// retry/dead-letter policy — implementations do NOT need their own
/// retry loop.
/// </summary>
public interface IOutboxPublisher
{
    /// <summary>
    /// Delivers one message to its transport. A thrown exception counts
    /// as a delivery failure and feeds the outbox row's bounded retry /
    /// dead-letter policy — implementations do not need their own retry
    /// loop.
    /// </summary>
    /// <remarks>
    /// No real implementation is wired by this change (design.md decision
    /// #6 — "nothing consumes it yet" until a later workstream registers
    /// one); the default registration is
    /// <see cref="NoopOutboxPublisher"/>.
    /// </remarks>
    /// <param name="type">Contract name — the same value the row was enqueued with.</param>
    /// <param name="payloadJson">Raw JSON payload.</param>
    /// <param name="cancellationToken"></param>
    public Task PublishAsync(string type, string payloadJson, CancellationToken cancellationToken);
}
