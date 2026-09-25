using Microsoft.Extensions.Logging;

namespace Comuki.Engine.Orchestration.Infrastructure.OutboxDispatch;

/// <summary>
/// Default <see cref="IOutboxPublisher"/> until a later workstream
/// registers a real transport. Marks every message dispatched without
/// actually delivering it — the durable store is the single source of
/// truth, so the row's <c>dispatched_at</c> stamp still drives the
/// dispatch sweep's "no pending rows" loop-exit. When a real publisher
/// is added, replace this registration in
/// <see cref="OrchestrationInfrastructureExtensions"/>.
/// </summary>
/// <param name="logger">Structured logger — Debug on every message marked dispatched.</param>
public sealed class NoopOutboxPublisher(ILogger<NoopOutboxPublisher> logger) : IOutboxPublisher
{
    /// <inheritdoc />
    public Task PublishAsync(string type, string payloadJson, CancellationToken cancellationToken)
    {
        logger.LogDebug("outbox message {Type} has no publisher wired yet; marking dispatched", type);
        return Task.CompletedTask;
    }
}
