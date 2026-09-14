using System.Data.Common;
using Comuki.Modules.Memory.Application.Ports;
using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>
/// The ephemeral-facts sweeper: deletes facts past their 14-day TTL on a
/// fixed interval (once at startup, then hourly — the TTL is days, an
/// hour of lag is nothing). Sweep failures are reported to the worker
/// registry, which logs and retries with backoff. This is the only thing
/// in the module that mutates memory on its own — everything else is an
/// explicit write/forget. It owns no subject, and
/// <see cref="MemoryDbContext"/>'s scope query filter would otherwise
/// hide every row it is meant to reap — <c>AsSystem</c> is the same
/// declaration <c>LeaseReaperComukiWorker</c> makes for the same reason.
/// </summary>
/// <param name="store">The memory store to sweep.</param>
/// <param name="scopeAccessor">Installs the system scope the sweep needs to see every subject's rows.</param>
/// <param name="clock">Injected to keep the cutoff testable.</param>
/// <param name="logger">Structured logger — Information on a non-zero count.</param>
public sealed class MemorySweepComukiWorker(
    IMemoryStore store,
    ISubjectScopeAccessor scopeAccessor,
    TimeProvider clock,
    ILogger<MemorySweepComukiWorker> logger) : IComukiWorker
{
    /// <summary>Sweep interval; the ephemeral TTL is 14 days so an hour of lag is noise.</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public string Name => "memory-sweep";

    /// <inheritdoc />
    public WorkerSchedule Schedule => WorkerSchedule.Interval(SweepInterval);

    /// <inheritdoc />
    public async Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        using var systemScope = scopeAccessor.AsSystem(Name);

        try
        {
            var swept = await store.SweepExpiredAsync(clock.GetUtcNow(), cancellationToken);
            if (swept > 0)
            {
                logger.LogInformation("Swept {SweptCount} expired ephemeral memory fact(s)", swept);
            }

            return WorkerResult.Ok($"swept {swept}", swept);
        }
        catch (Exception exception) when (exception is DbException or IOException or TimeoutException)
        {
            return WorkerResult.Fail($"sweep failed: {exception.Message}");
        }
    }
}
