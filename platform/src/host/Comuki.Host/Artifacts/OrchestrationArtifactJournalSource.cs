using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Artifacts.Domain;
using Comuki.Shared.Contracts.Artifacts;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Artifacts;

/// <summary>
/// EF-backed adapter that exposes the orchestration schema to the
/// artifacts module through <see cref="IRunArtifactJournalSource"/>:
/// terminal status snapshot (run row + last work item row) and
/// per-work-item brief. Lives in the host composition root so the
/// artifacts module never reaches into the engine schema.
/// </summary>
/// <param name="db">Scoped orchestration DbContext.</param>
/// <param name="scopeAccessor">Ambient scope — declares system scope for the journal reads.</param>
public sealed class OrchestrationArtifactJournalSource(
    OrchestrationDbContext db,
    ISubjectScopeAccessor scopeAccessor) : IRunArtifactJournalSource
{
    /// <inheritdoc />
    public async Task<RunTerminalSnapshot?> ReadTerminalAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        using var systemScope = scopeAccessor.AsSystem("artifact-journal-source");
        var run = await db.Runs
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

        if (run is null)
        {
            return null;
        }

        if (!ArtifactPackageTriggers.IsTerminal(RunStatusWire.Wire(run.Status)))
        {
            return null;
        }

        var latestWorkItemEvent = await db.RunEvents
            .AsNoTracking()
            .Where(runEvent => runEvent.RunId == runId)
            .Where(runEvent => runEvent.Type == RunEventTypes.WorkItemStatusChanged)
            .OrderByDescending(runEvent => runEvent.OccurredAt)
            .ThenByDescending(runEvent => runEvent.Id)
            .FirstOrDefaultAsync(cancellationToken);

        Guid? originWorkItemId = null;
        string? detailJson = null;
        if (latestWorkItemEvent is not null)
        {
            var parsed = System.Text.Json.JsonDocument.Parse(latestWorkItemEvent.Payload).RootElement;
            if (parsed.TryGetProperty("itemId", out var itemElement)
                && itemElement.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                originWorkItemId = Guid.Parse(itemElement.GetString()!);
            }

            if (parsed.TryGetProperty("detail", out var detailElement)
                && detailElement.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                detailJson = detailElement.GetRawText();
            }
        }

        return new RunTerminalSnapshot(
            RunId: runId.Value,
            Status: RunStatusWire.Wire(run.Status),
            OccurredAt: run.UpdatedAt,
            OriginWorkItemId: originWorkItemId,
            DetailJson: detailJson);
    }

    /// <inheritdoc />
    public async Task<string?> ReadWorkItemBriefAsync(
        Guid workItemId,
        CancellationToken cancellationToken = default)
    {
        var brief = await db.WorkItems
            .AsNoTracking()
            .Where(workItem => workItem.Id == workItemId)
            .Select(workItem => workItem.Brief)
            .FirstOrDefaultAsync(cancellationToken);

        return brief;
    }
}

/// <summary>
/// Lower-case run-status wire mapping — isolated so the adapter holds
/// only the journal reads (<c>code-shape.md</c> §1a). Every status name
/// round-trips through <c>ToString().ToLowerInvariant()</c>; the old
/// hand-written switch duplicated that mapping arm for arm.
/// </summary>
file static class RunStatusWire
{
    /// <summary>Lower-case wire string for a run status — the engine and the journal both use the same form.</summary>
    /// <param name="status">Engine status to map onto the wire form.</param>
    public static string Wire(RunStatus status)
    {
        return status.ToString().ToLowerInvariant();
    }
}
