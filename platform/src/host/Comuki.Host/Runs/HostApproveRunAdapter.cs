using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Contracts.Runs;
using Comuki.Shared.Kernel.Exceptions;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Runs;

/// <summary>
/// Host-side <see cref="IApproveRunPort"/>: the run-approve decision
/// transitions <c>Escalated</c> → <c>Running</c> and appends the journal
/// row. Runs the read/mutation as system — the caller is an authenticated
/// subject, but the row's project may not be in their scope at the moment
/// (e.g. a deleted role mid-flow); the controller already enforced
/// <c>run:read</c>, and the resulting <c>Running</c> row is project-local
/// to the orchestrator — the engine schema is the system side of the world.
/// </summary>
/// <param name="db">Scoped orchestration DbContext.</param>
/// <param name="scopeAccessor">Ambient scope — declare system for the run.</param>
/// <param name="clock">Time source for the transition stamp and the journal row.</param>
public sealed class HostApproveRunAdapter(
    OrchestrationDbContext db,
    ISubjectScopeAccessor scopeAccessor,
    TimeProvider clock) : IApproveRunPort
{
    /// <inheritdoc />
    public async Task ApproveAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        using var systemScope = scopeAccessor.AsSystem("runs-approve");

        var run = await db.Runs.FirstOrDefaultAsync(run => run.Id == runId, cancellationToken) ?? throw new ProviderNotFoundException(
                "run.not_found",
                $"run '{runId.Value}' not found");
        if (!RunTransitions.IsLegal(run.Status, RunStatus.Running))
        {
            // "approve" reads as "release the gate". The transition is legal
            // only from Escalated — every other source, including terminal
            // states, is a state conflict (see RunsEndpointRunner / chat parity).
            throw new RunDecisionConflictException(
                run.Status,
                RunStatus.Running,
                "approve");
        }

        var from = run.Status;
        var now = clock.GetUtcNow();
        run.TransitionTo(RunStatus.Running, now);

        var payload = JsonSerializer.Serialize(
            new RunStatusChangedPayload(from.ToString(), RunStatus.Running.ToString(), Actor: "operator", Reason: null),
            JsonSerializerOptions.Web);

        db.RunEvents.Add(RunEvent.Create(runId, RunEventTypes.RunStatusChanged, payload, now));

        await db.SaveChangesAsync(cancellationToken);
    }
}
