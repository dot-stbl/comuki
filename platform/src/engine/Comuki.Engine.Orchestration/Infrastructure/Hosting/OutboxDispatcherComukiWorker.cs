using Comuki.Engine.Orchestration.Infrastructure.OutboxDispatch;
using Comuki.Engine.Orchestration.Options;
using Comuki.Shared.Bootstrap.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Orchestration.Infrastructure.Hosting;

/// <summary>
/// Hosted outbox dispatcher: every <see cref="OutboxOptions.DispatchInterval"/>
/// it runs one <see cref="OutboxDispatcher.DispatchAsync"/> sweep in a
/// fresh DI scope (the DbContext is scoped). Dispatch failures are reported
/// to the worker registry, which logs and retries with backoff — the host
/// keeps running. The outbox/inbox tables carry no subject-scope query
/// filter, so the worker does not need to install a system scope (unlike
/// <c>LeaseReaperComukiWorker</c>).
/// </summary>
/// <param name="scopeFactory">Opens the per-cycle scope (the dispatcher's DbContext is scoped).</param>
/// <param name="outboxOptions">Bound from <c>Orchestration:Outbox</c>; the dispatch interval drives the schedule.</param>
/// <param name="logger">Structured logger — Information on a non-zero count.</param>
public sealed class OutboxDispatcherComukiWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> outboxOptions,
    ILogger<OutboxDispatcherComukiWorker> logger) : IComukiWorker
{
    /// <inheritdoc />
    public string Name => "outbox-dispatcher";

    /// <inheritdoc />
    public WorkerSchedule Schedule => WorkerSchedule.Interval(outboxOptions.Value.DispatchInterval);

    /// <inheritdoc />
    public async Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();

        var (dispatched, deadLettered) = await dispatcher.DispatchAsync(cancellationToken);
        if (dispatched + deadLettered > 0)
        {
            logger.LogInformation(
                "Outbox dispatcher dispatched {DispatchedCount} message(s), dead-lettered {DeadLetteredCount}",
                dispatched,
                deadLettered);
        }

        return WorkerResult.Ok($"dispatched {dispatched}, dead-lettered {deadLettered}", new { dispatched, deadLettered });
    }
}
