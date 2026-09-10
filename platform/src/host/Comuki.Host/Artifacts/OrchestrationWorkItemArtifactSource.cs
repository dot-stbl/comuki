using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Artifacts.Application.VisualArtifacts.Ports;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Artifacts;

/// <summary>
/// Host-composed implementation of <see cref="IWorkItemArtifactSource"/>.
/// Probes the orchestration DbContext for a work item leased to the
/// presenting worker and returns its parent project + run — the visual
/// artifact store uses the project to scope the object key and the run
/// to journal the <c>artifact.published</c> event. Scope-decorated
/// AsSystem because the host is the one calling this for the worker;
/// the worker token itself is the lease authority.
/// </summary>
/// <param name="db">Scoped orchestration DbContext.</param>
/// <param name="scopeAccessor">Ambient subject scope — declared AsSystem for the read.</param>
public sealed class OrchestrationWorkItemArtifactSource(
    OrchestrationDbContext db,
    ISubjectScopeAccessor scopeAccessor) : IWorkItemArtifactSource
{
    /// <inheritdoc />
    public async Task<WorkItemOwnership?> FindOwnedAsync(
        Guid workItemId,
        WorkerId workerId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // The worker runtime is a platform-system consumer: workers
        // reach into any project they were leased on, so the subject
        // scope must be unrestricted for the lookup itself.
        using var systemScope = scopeAccessor.AsSystem("worker-artifact-source");

        var lease = await db.WorkItems
            .AsNoTracking()
            .Where(workItem => workItem.Id == workItemId)
            .Where(workItem => workItem.LeasedBy != null && workItem.LeasedBy == workerId)
            .Where(workItem => workItem.LeaseUntil != null && workItem.LeaseUntil > now)
            .Select(workItem => new { workItem.RunId })
            .FirstOrDefaultAsync(cancellationToken)
            ;
        return lease is null
            ? null
            : await OrchestrationWorkItemArtifactSourceHelpers.ResolveProjectAsync(db, lease.RunId, cancellationToken);
    }
}

/// <summary>
/// File-static helpers for <see cref="OrchestrationWorkItemArtifactSource"/>.
/// The source class holds only the public port implementation; the
/// per-run project lookup lives here so the public class does not
/// grow private business logic (per
/// <c>class-layout-and-tooling.md §1a</c>).
/// </summary>
file static class OrchestrationWorkItemArtifactSourceHelpers
{
    /// <summary>Looks up the project id of one run and projects both ids onto <see cref="WorkItemOwnership"/>.</summary>
    /// <param name="db">Orchestration DbContext (same scope as the caller).</param>
    /// <param name="runId">Run the worker is leased to.</param>
    /// <param name="cancellationToken"></param>
    public static async Task<WorkItemOwnership?> ResolveProjectAsync(
        OrchestrationDbContext db,
        RunId runId,
        CancellationToken cancellationToken)
    {
        var run = await db.Runs
            .AsNoTracking()
            .Where(run => run.Id == runId)
            .Select(run => new { run.ProjectId })
            .FirstOrDefaultAsync(cancellationToken)
            ;
        return run is null
            ? null
            : new WorkItemOwnership(new ProjectId(run.ProjectId.Value), runId);
    }
}
