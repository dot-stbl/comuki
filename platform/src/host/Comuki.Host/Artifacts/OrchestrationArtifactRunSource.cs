using System.Runtime.CompilerServices;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Artifacts.Application.Packaging;
using Comuki.Modules.Artifacts.Domain;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Artifacts;

/// <summary>
/// Host-side <see cref="IRunArtifactRunSource"/>: lists runs whose terminal
/// status has not been packaged yet, ordered oldest transition first.
/// The artifacts module never reaches into the engine schema — this adapter
/// is the only path from the packager to the orchestration tables. Scoped
/// to align with the per-request <see cref="OrchestrationDbContext"/>; the
/// scope axis is declared <c>AsSystem("artifact-packager")</c> on every
/// call so the row-level query filters do not gate the read.
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
/// <param name="scopeAccessor">Ambient subject scope — declared system for the read.</param>
public sealed class OrchestrationArtifactRunSource(
    OrchestrationDbContext db,
    ISubjectScopeAccessor scopeAccessor) : IRunArtifactRunSource
{
    /// <summary>
    /// Terminal RunStatus values the packager considers a bundle candidate.
    /// Mirrors <see cref="ArtifactPackageTriggers.IsTerminal"/> on the
    /// wire-string side, with one extra enum member the wire string does not
    /// carry (<see cref="RunStatus.Queued"/> never ends up here because the
    /// run lifecycle never lands a queued run into the orchestration table
    /// without the first transition).
    /// </summary>
    private static readonly RunStatus[] terminalStatusValues =
    [
        RunStatus.Succeeded,
        RunStatus.Failed,
        RunStatus.Cancelled,
        RunStatus.Escalated,
    ];

    /// <inheritdoc />
    public async IAsyncEnumerable<RunArtifactCandidate> ListUnbundledTerminalAsync(
        int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var systemScope = scopeAccessor.AsSystem("artifact-packager");

        var candidates = await db.Runs
            .AsNoTracking()
            .Where(run => terminalStatusValues.Contains(run.Status))
            .OrderBy(run => run.UpdatedAt)
            .ThenBy(run => run.Id)
            .Take(limit)
            .Select(run => new { run.Id, run.ProjectId })
            .ToListAsync(cancellationToken);

        foreach (var row in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new RunArtifactCandidate(row.Id, row.ProjectId);
        }
    }

    /// <inheritdoc />
    public async Task<ProjectId?> ReadProjectIdAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        using var systemScope = scopeAccessor.AsSystem("artifact-packager");

        var projectId = await db.Runs
            .AsNoTracking()
            .Where(run => run.Id == runId)
            .Select(run => (ProjectId?)run.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);

        return projectId;
    }
}
