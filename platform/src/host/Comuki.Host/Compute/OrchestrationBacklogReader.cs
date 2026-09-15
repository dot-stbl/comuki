using Comuki.Engine.Compute.Ports;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Compute;

/// <summary>
/// Adapter from the orchestration work-item queue to the compute
/// engine's backlog signal. Resolves the DbContext factory lazily so
/// build-time host introspection (OpenAPI generation, no DB) does not
/// fail DI validation — a missing factory reads as zero backlog.
/// </summary>
/// <param name="serviceProvider"></param>
public sealed class OrchestrationBacklogReader(IServiceProvider serviceProvider) : IBacklogReader
{
    /// <inheritdoc />
    public async Task<int> CountQueuedAsync(
        ProjectId projectId,
        string? profileKey,
        CancellationToken cancellationToken = default)
    {
        var factory = serviceProvider.GetService<IDbContextFactory<OrchestrationDbContext>>();
        if (factory is null)
        {
            return 0;
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.WorkItems
            .Where(item => item.Status == Engine.Orchestration.Domain.WorkItemStatus.Queued)
            .Where(item => db.Runs.Any(run => run.Id == item.RunId && run.ProjectId == projectId))
            .Where(item => profileKey == null || item.ProfileKey == profileKey)
            .CountAsync(cancellationToken);
    }
}
