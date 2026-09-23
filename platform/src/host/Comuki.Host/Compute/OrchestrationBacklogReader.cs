using Comuki.Engine.Compute.Ports;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Compute;

/// <summary>
/// Adapter from the orchestration work-item queue to the compute
/// engine's backlog signal. The reader is a singleton but the
/// <see cref="OrchestrationDbContext"/> is scoped, so each read opens its
/// own DI scope — the same shape <c>LeaseReaperComukiWorker</c> uses.
/// Resolves the context from the root provider lazily so build-time host
/// introspection (OpenAPI generation, no DB) does not fail DI validation —
/// a missing registration reads as zero backlog.
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
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetService<OrchestrationDbContext>();
        return db is null
            ? 0
            : await db.WorkItems
            .Where(item => item.Status == Engine.Orchestration.Domain.WorkItemStatus.Queued)
            .Where(item => db.Runs.Any(run => run.Id == item.RunId && run.ProjectId == projectId))
            .Where(item => profileKey == null || item.ProfileKey == profileKey)
            .CountAsync(cancellationToken);
    }
}
