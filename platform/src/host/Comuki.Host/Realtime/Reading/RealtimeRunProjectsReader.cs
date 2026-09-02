using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Realtime.Reading;

/// <summary>
/// <see cref="IRealtimeRunProjects"/> over the orchestration context: one
/// no-tracking batch read. The lookup declares itself a system consumer —
/// it reads run→project metadata only; the access decision is made
/// separately by the caller from the evaluator's answer, so this never
/// widens anything.
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
/// <param name="scopeAccessor">Ambient scope — the read runs as system.</param>
public sealed class RealtimeRunProjectsReader(OrchestrationDbContext db, ISubjectScopeAccessor scopeAccessor)
    : IRealtimeRunProjects
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<RunId, ProjectId>> ReadAsync(
        IReadOnlyCollection<RunId> runIds,
        CancellationToken cancellationToken = default)
    {
        var ids = runIds.ToArray();
        using var systemScope = scopeAccessor.AsSystem("realtime");

        return await db.Runs.AsNoTracking()
            .Where(run => ids.Contains(run.Id))
            .ToDictionaryAsync(
                static run => run.Id,
                static run => run.ProjectId,
                cancellationToken);
    }
}
