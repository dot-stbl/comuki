using Comuki.Engine.Compute.Ports;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Compute;

/// <summary>
/// Adapter from the orchestration work-item queue to the compute
/// engine's backlog signal. Counts claimable (Queued) items per project,
/// optionally narrowed to one profile key.
/// </summary>
/// <param name="contextFactory"></param>
public sealed class OrchestrationBacklogReader(
    IDbContextFactory<OrchestrationDbContext> contextFactory) : IBacklogReader
{
    /// <inheritdoc />
    public async Task<int> CountQueuedAsync(
        ProjectId projectId,
        string? profileKey,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.WorkItems
            .Where(item => item.Status == Engine.Orchestration.Domain.WorkItemStatus.Queued)
            .Where(item => db.Runs.Any(run => run.Id == item.RunId && run.ProjectId == projectId))
            .Where(item => profileKey == null || item.ProfileKey == profileKey)
            .CountAsync(cancellationToken);
    }
}
