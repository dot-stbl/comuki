using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Artifacts.Application.Packaging;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Artifacts;

/// <summary>
/// EF-backed adapter that exposes terminal-but-unbundled runs to the
/// artifacts module through <see cref="IRunArtifactRunSource"/>. Lives
/// in the host composition root so the artifacts module never reaches
/// into the engine schema. The packager iterates the candidates the
/// engine hands back — the join against the bundle store stays in SQL,
/// scoped to terminal statuses the module treats as triggers.
/// </summary>
/// <param name="db">Scoped orchestration DbContext.</param>
/// <param name="bundleStore">Already-bundled bookkeeping in the artifacts schema.</param>
public sealed class OrchestrationArtifactRunSource(
    OrchestrationDbContext db,
    IRunArtifactBundleStore bundleStore) : IRunArtifactRunSource
{
    /// <inheritdoc />
    public async IAsyncEnumerable<RunArtifactCandidate> ListUnbundledTerminalAsync(
        int limit,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var terminals = new[] { RunStatus.Succeeded, RunStatus.Failed, RunStatus.Cancelled, RunStatus.Escalated };

        await foreach (var run in db.Runs
            .AsNoTracking()
            .Where(run => terminals.Contains(run.Status))
            .OrderBy(run => run.UpdatedAt)
            .AsAsyncEnumerable())
        {
            if (await bundleStore.IsBundledAsync(run.Id.Value, cancellationToken))
            {
                continue;
            }

            yield return new RunArtifactCandidate(run.Id, run.ProjectId);

            if (--limit <= 0)
            {
                yield break;
            }
        }
    }

    /// <inheritdoc />
    public Task<ProjectId?> ReadProjectIdAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        return db.Runs
            .AsNoTracking()
            .Where(run => run.Id == runId)
            .Select(run => (ProjectId?)run.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
