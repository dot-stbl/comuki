using Comuki.Engine.Orchestration.Infrastructure.Leases;
using Comuki.Engine.Orchestration.Options;
using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Orchestration.Infrastructure.Hosting;

/// <summary>
/// Hosted lease reaper: every <see cref="LeaseOptions.ReapInterval"/> it runs
/// one <see cref="LeaseReaper.ReapAsync"/> sweep in a fresh DI scope (the
/// DbContext is scoped). Reaper failures are reported to the worker
/// registry, which logs and retries with backoff — the host keeps
/// running. The sweep runs as a named system consumer: it owns no
/// subject, and the scope query filters would otherwise hide the expired
/// rows it reaps.
/// </summary>
/// <param name="scopeFactory">Opens the per-cycle scope (the reaper's DbContext is scoped).</param>
/// <param name="scopeAccessor">Installs the system scope the reaper needs to see every subject's rows.</param>
/// <param name="leaseOptions">Bound from <c>Orchestration:Lease</c>; the reap interval drives the schedule.</param>
/// <param name="logger">Structured logger — Information on a non-zero count.</param>
public sealed class LeaseReaperComukiWorker(
    IServiceScopeFactory scopeFactory,
    ISubjectScopeAccessor scopeAccessor,
    IOptions<LeaseOptions> leaseOptions,
    ILogger<LeaseReaperComukiWorker> logger) : IComukiWorker
{
    /// <inheritdoc />
    public string Name => "lease-reaper";

    /// <inheritdoc />
    public WorkerSchedule Schedule => WorkerSchedule.Interval(leaseOptions.Value.ReapInterval);

    /// <inheritdoc />
    public async Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        using var systemScope = scopeAccessor.AsSystem(Name);
        var reaper = scope.ServiceProvider.GetRequiredService<LeaseReaper>();

        var reaped = await reaper.ReapAsync(cancellationToken);
        if (reaped.Count > 0)
        {
            logger.LogInformation("Reaped {ReapedCount} expired work item lease(s)", reaped.Count);
        }

        return WorkerResult.Ok($"reaped {reaped.Count}", reaped.Count);
    }
}
