namespace Comuki.Shared.Contracts;

/// <summary>
/// Minimal outbox port producers publish integration events through.
/// Consumers live in different modules (Procedures publishes
/// <c>procedures.procedure.published.v1</c>); the host wires this to
/// the platform's actual outbox implementation — producers do not
/// depend on the outbox package directly, they depend on this
/// one-method seam and the host closes the loop.
///
/// <para>
/// The seam is deliberately thin: a single generic <c>Publish</c>
/// method that carries the event payload and an event-type name.
/// The wire-format envelope (versioning, partitioning, dedupe keys)
/// lives on the host-side outbox implementation, not in the producer.
/// </para>
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Publishes <paramref name="payload"/> under the named
    /// <paramref name="eventType"/>. The host's outbox implementation
    /// is responsible for at-least-once delivery, ordering within a
    /// partition, and the on-disk envelope shape.
    /// </summary>
    /// <param name="eventType">Stable dot.case event name (e.g. <c>procedures.procedure.published.v1</c>).</param>
    /// <param name="payload">The event payload — usually a record type the host knows how to serialize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task PublishAsync(
        string eventType,
        object payload,
        CancellationToken cancellationToken = default);
}
