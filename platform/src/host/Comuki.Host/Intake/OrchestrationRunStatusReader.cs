using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Intake.Application.Ports.Admission;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Intake;

/// <summary>
/// Host-side <see cref="IRunStatusReader"/>: batch status reads over
/// the orchestration context for the run status bridge. Registered
/// scoped — one context per bridge scope.
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
public sealed class OrchestrationRunStatusReader(OrchestrationDbContext db) : IRunStatusReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<RunId, string>> ReadStatusesAsync(
        IReadOnlyCollection<RunId> runIds,
        CancellationToken cancellationToken = default)
    {
        var ids = runIds.ToArray();

        return await db.Runs.AsNoTracking()
            .Where(run => ids.Contains(run.Id))
            .ToDictionaryAsync(
                static run => run.Id,
                static run => run.Status.ToString(),
                cancellationToken);
    }
}
