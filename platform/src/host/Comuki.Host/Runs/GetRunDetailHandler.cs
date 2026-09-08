using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Runs;

/// <summary>
/// Read-side handler behind <c>GET /api/v1/runs/{runId}</c>. Returns the
/// full detail envelope (work items with their DAG edges, the recent
/// journal, and the pinned revisions) or <c>null</c> when the row is not
/// visible to the current subject (out of scope or truly absent) — the
/// controller maps <c>null</c> to a 404 ProblemDetails.
///
/// Out-of-scope rows surface as 404 by design: the EF global query filter
/// on <see cref="OrchestrationDbContext.Runs"/> / <see cref="OrchestrationDbContext.WorkItems"/>
/// strips them before the SQL sees them, so the request reads as "not
/// found" — the same shape the user would see for a deleted run. The
/// action-axis / object-axis split keeps permission filters responsible
/// only for the action; the scope query filter is responsible only for
/// the object, and surfaces misses as 404.
///
/// Several <see cref="RunDetail"/> fields are populated as placeholders
/// because the platform does not yet store the data — see
/// <see cref="RunDetail"/> for the field-by-field story.
/// </summary>
/// <param name="db">Scoped orchestration context.</param>
public sealed class GetRunDetailHandler(OrchestrationDbContext db)
{
    /// <summary>How many journal rows to fetch for the events strip.</summary>
    private const int EventLimit = 20;

    /// <summary>
    /// Looks up the run, builds the detail envelope. Returns <c>null</c>
    /// when the row is not visible to the current subject (out of scope or
    /// truly absent).
    /// </summary>
    /// <param name="runId">Run to read.</param>
    /// <param name="cancellationToken"></param>
    public async Task<RunDetail?> GetAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        // The scope filter on Runs/WorkItems is global and parameterised on
        // the context instance — declaring a system scope here is unnecessary
        // and would widen visibility. We rely on the host middleware to set
        // the subject scope for the request.

        var run = await db.Runs
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == runId, cancellationToken);

        if (run is null)
        {
            return null;
        }

        var workItemRows = await db.WorkItems
            .AsNoTracking()
            .Where(item => item.RunId == runId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        var workItemIds = workItemRows.ConvertAll(static row => row.Id);

        var dependencies = workItemIds.Count == 0
            ? []
            : await db.WorkItemDependencies
                .AsNoTracking()
                .Where(edge => workItemIds.Contains(edge.WorkItemId))
                .GroupBy(edge => edge.WorkItemId)
                .Select(grouping => new
                {
                    WorkItemId = grouping.Key,
                    DependsOn = grouping.Select(edge => edge.DependsOnWorkItemId).ToList(),
                })
                .ToDictionaryAsync(
                    static row => row.WorkItemId,
                    static row => (IReadOnlyList<Guid>)row.DependsOn,
                    cancellationToken);

        var workItems = workItemRows
            .ConvertAll(row => new RunDetailWorkItem(
                Id: row.Id,
                Profile: row.ProfileKey,
                Label: string.Empty,
                Status: GetRunDetailMappings.WireItemStatus(row.Status),
                DependsOn: dependencies.GetValueOrDefault(row.Id, []),
                Cost: 0m,
                Tokens: 0L,
                StartedAt: null));

        var events = await db.RunEvents
            .AsNoTracking()
            .Where(runEvent => runEvent.RunId == runId)
            .OrderByDescending(runEvent => runEvent.OccurredAt)
            .ThenByDescending(runEvent => runEvent.Id)
            .Take(EventLimit)
            .Select(runEvent => new
            {
                runEvent.Id,
                runEvent.Type,
                runEvent.OccurredAt,
                runEvent.Payload,
            })
            .ToListAsync(cancellationToken);

        var eventViews = events
            .ConvertAll(entry => new RunDetailEvent(
                Id: entry.Id,
                WorkItemId: null,
                Type: entry.Type,
                OccurredAt: entry.OccurredAt,
                PayloadJson: entry.Payload));

        // Pin the run to the image + profiles ref of its first work item —
        // runs today have one work item profile at apply time, so the
        // "first row" reads as the run's canonical pin. A multi-profile run
        // (graphs, fan-out) would need a per-work-item revision map; out
        // of scope for this slice.
        var firstWorkItem = workItemRows.Count > 0 ? workItemRows[0] : null;

        return new RunDetail(
            Id: run.Id.Value,
            ProjectId: run.ProjectId.Value,
            Status: GetRunDetailMappings.WireRunStatus(run.Status),
            CreatedAt: run.CreatedAt,
            UpdatedAt: run.UpdatedAt,
            Title: string.Empty,
            App: firstWorkItem?.Image ?? string.Empty,
            Model: "worker",
            CostUsd: 0m,
            Tokens: 0L,
            Brief: firstWorkItem?.Brief ?? string.Empty,
            WorkItems: workItems,
            Events: eventViews,
            Rules: [],
            Revision: new RunDetailRevision(
                Rules: firstWorkItem?.ProfilesRef ?? string.Empty,
                Sdk: firstWorkItem?.Image ?? string.Empty));
    }
}

/// <summary>
/// Engine enum → wire-string mappings for the run-detail handler. The wire
/// form is the same lower-case enum name the journal and the artifact
/// packager already use; keeping the convention here lets the FE map wire
/// statuses back to its <c>Status</c> union without ad-hoc translation.
/// </summary>
file static class GetRunDetailMappings
{
    /// <summary>Lower-case <see cref="RunStatus"/> string for the wire.</summary>
    /// <param name="status">Engine status.</param>
    public static string WireRunStatus(RunStatus status)
    {
        return status.ToString().ToLowerInvariant();
    }

    /// <summary>Lower-case <see cref="WorkItemStatus"/> string for the wire.</summary>
    /// <param name="status">Engine status.</param>
    public static string WireItemStatus(WorkItemStatus status)
    {
        return status.ToString().ToLowerInvariant();
    }
}
