using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Artifacts.Domain;
using Comuki.Shared.Contracts.Artifacts;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Artifacts;

/// <summary>
/// Host-side <see cref="IRunArtifactJournalSource"/>: reads the terminal
/// snapshot and the work-item brief straight from the orchestration schema.
/// Registered scoped so the existing per-request <see cref="OrchestrationDbContext"/>
/// flows in. The scope axis is declared <c>AsSystem("artifact-packager")</c>
/// on every call so the row-level query filters do not gate the read —
/// the packager is a system consumer.
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
/// <param name="scopeAccessor">Ambient subject scope — declared system for the read.</param>
public sealed class OrchestrationArtifactJournalSource(
    OrchestrationDbContext db,
    ISubjectScopeAccessor scopeAccessor) : IRunArtifactJournalSource
{
    /// <inheritdoc />
    public async Task<RunTerminalSnapshot?> ReadTerminalAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        using var systemScope = scopeAccessor.AsSystem("artifact-packager");

        var run = await db.Runs
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == runId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        var status = run.Status.ToString().ToLowerInvariant();
        if (!ArtifactPackageTriggers.IsTerminal(status))
        {
            return null;
        }

        var originWorkItemId = await db.WorkItems
            .AsNoTracking()
            .Where(item => item.RunId == runId)
            .OrderBy(item => item.CreatedAt)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Latest worker report — the authoritative result, when one was
        // journaled. Null when the run has no worker report (operator-driven
        // cancel, manual transition with no payload).
        var detailJson = await db.RunEvents
            .AsNoTracking()
            .Where(runEvent => runEvent.RunId == runId
                && runEvent.Type == RunEventTypes.WorkerReported)
            .OrderByDescending(runEvent => runEvent.OccurredAt)
            .ThenByDescending(runEvent => runEvent.Id)
            .Select(runEvent => runEvent.Payload)
            .FirstOrDefaultAsync(cancellationToken);

        return new RunTerminalSnapshot(
            RunId: run.Id.Value,
            Status: status,
            OriginWorkItemId: originWorkItemId,
            DetailJson: string.IsNullOrWhiteSpace(detailJson) ? null : detailJson,
            OccurredAt: run.UpdatedAt);
    }

    /// <inheritdoc />
    public async Task<string?> ReadWorkItemBriefAsync(Guid workItemId, CancellationToken cancellationToken = default)
    {
        using var systemScope = scopeAccessor.AsSystem("artifact-packager");

        var brief = await db.WorkItems
            .AsNoTracking()
            .Where(item => item.Id == workItemId)
            .Select(item => item.Brief)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(brief) ? null : brief;
    }
}
