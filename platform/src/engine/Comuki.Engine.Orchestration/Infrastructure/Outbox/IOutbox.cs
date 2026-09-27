namespace Comuki.Engine.Orchestration.Infrastructure.Outbox;

/// <summary>
/// Stages a message for durable at-least-once delivery. The implementation
/// is expected to add the row to the caller's <c>DbContext</c> so the
/// caller's own <c>SaveChangesAsync</c> commits the outbox row atomically
/// with the aggregate change it reports. <see cref="Enqueue"/> itself
/// does NOT call <c>SaveChangesAsync</c>.
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Stages a message for durable at-least-once delivery in the SAME unit
    /// of work as the caller — call this, then let the caller's own
    /// <c>SaveChangesAsync</c> commit it atomically with the aggregate
    /// change. Does not call <c>SaveChangesAsync</c> itself.
    /// </summary>
    /// <param name="type">Contract name — a stable dot.case string.</param>
    /// <param name="payloadJson">Raw JSON payload (matches the <c>jsonb</c> column shape).</param>
    public void Enqueue(string type, string payloadJson);
}
