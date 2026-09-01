using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Contracts.Runs;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Chat;

/// <summary>
/// Host-side <see cref="IRunsReader"/>: reads the newest runs straight from
/// the orchestration context. Registered scoped — one context per request;
/// the chat tool executor resolves it inside its own scope.
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
public sealed class OrchestrationRunsReader(OrchestrationDbContext db) : IRunsReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<RunSummary>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        var runs = await db.Runs.AsNoTracking()
            .OrderByDescending(run => run.UpdatedAt)
            .Take(limit)
            .Select(static run => new RunSummary(
                run.Id.Value,
                run.ProjectId.Value,
                run.Status.ToString().ToLowerInvariant(),
                run.CreatedAt,
                run.UpdatedAt))
            .ToListAsync(cancellationToken);

        return runs;
    }
}
