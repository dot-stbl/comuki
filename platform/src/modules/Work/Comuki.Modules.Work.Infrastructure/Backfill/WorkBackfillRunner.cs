using Comuki.Modules.Work.Application.Admission;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Completion;
using Comuki.Modules.Work.Domain.Sources;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Work.Infrastructure.Backfill;

/// <summary>
/// One-shot backfill runner for the cutover from pre-<c>WorkTask</c>
/// state (<c>add-work-management</c> tasks 6.1–6.6). Walks the
/// inbound items the Work module can map, creates one Task per
/// inbound id, and stamps the inbound-id-to-Task binding. The
/// runner is fully idempotent — a re-run finds every inbound id
/// already mapped and no-ops via
/// <see cref="IInboundItemBindingStore.FindByInboundAsync"/>.
/// <para>
/// The runner is a thin orchestrator — the inbound id walk +
/// per-row processing stays here; the persistence seam is the
/// existing <see cref="IWorkTaskStore"/> and
/// <see cref="IInboundItemBindingStore"/>. The runner does NOT
/// publish outbox events — the Work admission subscriber picks
/// up the new Task state through the WorkDbContext scope
/// lifecycle, and the legacy sync-bridge drains
/// <c>integrations.sync_jobs</c> on its own schedule (the runner
/// never flips the <c>work.management.sync.enabled</c> flag;
/// that's the operator's step, gated on this runner reporting
/// <c>NewTasksCreated = 0</c>).
/// </para>
/// </summary>
public sealed class WorkBackfillRunner(
    IWorkTaskStore store,
    IInboundItemBindingStore bindings,
    Func<int, CancellationToken, Task<IReadOnlyList<InboundItemBackfillSnapshot>>> sourceProvider,
    ILogger<WorkBackfillRunner> logger)
{
    /// <summary>Runs the backfill. Returns the per-row outcomes per the cutover matrix.</summary>
    public async Task<BackfillOutcome> RunAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var inboundItems = await sourceProvider(500, cancellationToken);

        var newTasks = 0;
        var skipped = 0;
        var unsupported = 0;

        foreach (var inbound in inboundItems)
        {
            if (!WorkTaskSourceKind.TryFromWire(inbound.ProviderWire, out var sourceKind))
            {
                logger.LogWarning(
                    "WorkBackfillRunner skipping inbound {ExternalId}: provider wire '{Provider}' is not a recognised WorkTaskSourceKind",
                    inbound.ExternalId, inbound.ProviderWire);
                unsupported++;
                continue;
            }

            var existing = await bindings.FindByInboundAsync(inbound.ExternalId, cancellationToken);
            if (existing is not null)
            {
                skipped++;
                continue;
            }

            var primary = WorkTaskSourceRef.Primary(
                sourceKind,
                inbound.ExternalId,
                inbound.Title);
            var task = WorkTask.Create(
                inbound.ProjectId,
                inbound.Title,
                inbound.Body ?? string.Empty,
                primary,
                WorkTaskCompletionPolicy.Default(now),
                now);

            await store.SaveAsync(task, cancellationToken);
            await bindings.BindAsync(inbound.ExternalId, task.Id, cancellationToken);
            newTasks++;
        }

        logger.LogInformation(
            "WorkBackfillRunner finished: created {Created} new task(s), skipped {Skipped} inbound(s), rejected {Unsupported} unsupported provider(s)",
            newTasks, skipped, unsupported);

        return new BackfillOutcome(newTasks, skipped, unsupported);
    }
}

/// <summary>Wire-form snapshot of an inbound item the migrator walks — decoupled from the Integrations domain type so the runner can be a test seam.</summary>
public sealed record InboundItemBackfillSnapshot(
    string ExternalId,
    string Title,
    string Body,
    string ProviderWire,
    ProjectId ProjectId);

/// <summary>Per-run outcome — the operator sees these three numbers and decides whether to flip the work-management sync gate.</summary>
public sealed record BackfillOutcome(int NewTasksCreated, int SkippedInboundItems, int UnsupportedProviders);
