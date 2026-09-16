using System.Data.Common;
using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Infrastructure.Configuration;
using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>
/// The memory sleep cycle: every <see cref="MemoryConsolidationOptions.Interval"/>
/// (default 6h) it promotes ephemeral facts that proved useful
/// (<c>read_count</c> ≥ <see cref="MemoryConsolidationOptions.PromoteReadThreshold"/>,
/// at least <see cref="PromoteMinAge"/> old) to standing, and decays
/// standing facts nobody read for <see cref="MemoryConsolidationOptions.DecayDays"/>
/// back to ephemeral with one day of grace before the sweep reaps them
/// (<see cref="Domain.Facts.MemoryFactPolicy.DecayRemainingTtl"/>). The
/// dedup-by-embedding pass is deliberately out of scope for this cycle.
/// Like the sweep worker it owns no subject, so it declares
/// <c>AsSystem</c> for the store's scope query filter. A DB-layer failure
/// is a failing <see cref="WorkerResult"/> — the registry backs off and
/// retries; a partial run is safe: both passes are idempotent set updates.
/// </summary>
/// <param name="store">The memory store to consolidate.</param>
/// <param name="options">Thresholds and interval.</param>
/// <param name="scopeAccessor">Installs the system scope the pass needs to see every subject's rows.</param>
/// <param name="clock">Injected to keep the cutoffs testable.</param>
/// <param name="logger">Structured logger — Information on a non-zero pass.</param>
public sealed class MemoryConsolidationComukiWorker(
    IMemoryStore store,
    IOptions<MemoryConsolidationOptions> options,
    ISubjectScopeAccessor scopeAccessor,
    TimeProvider clock,
    ILogger<MemoryConsolidationComukiWorker> logger) : IComukiWorker
{
    /// <summary>
    /// Minimum age before an ephemeral fact is promotable: a fresh write's
    /// own digest reads must not promote it in the same breath it landed.
    /// </summary>
    public static readonly TimeSpan PromoteMinAge = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public string Name => "memory-consolidation";

    /// <inheritdoc />
    public WorkerSchedule Schedule => WorkerSchedule.Interval(options.Value.Interval);

    /// <inheritdoc />
    public async Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        using var systemScope = scopeAccessor.AsSystem(Name);
        var settings = options.Value;
        var now = clock.GetUtcNow();

        try
        {
            var promoted = await store.PromoteReadFactsAsync(now, settings.PromoteReadThreshold, PromoteMinAge, cancellationToken);
            var decayed = await store.DecayUnreadFactsAsync(now, TimeSpan.FromDays(settings.DecayDays), cancellationToken);
            var total = await store.CountActiveFactsAsync(cancellationToken);

            if (promoted > 0 || decayed > 0)
            {
                logger.LogInformation(
                    "Consolidated memory facts: {PromotedCount} promoted, {DecayedCount} decayed, {TotalCount} active",
                    promoted, decayed, total);
            }

            return WorkerResult.Ok(
                $"promoted {promoted}, decayed {decayed}, {total} active",
                new MemoryConsolidationCounters(promoted, decayed, total));
        }
        catch (Exception exception) when (exception is DbException or IOException or TimeoutException)
        {
            return WorkerResult.Fail($"consolidation failed: {exception.Message}");
        }
    }
}
