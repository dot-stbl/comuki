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
/// Host-side <see cref="ICancelRunPort"/>: the operator-initiated cancel
/// transitions any non-terminal run to <c>Cancelled</c> and appends the
/// journal row in the same transaction. Terminal runs (<c>Succeeded</c>,
/// <c>Cancelled</c>) raise <see cref="RunDecisionConflictException"/> — the
/// caller asked to cancel what is already done. An optional
/// <paramref name="reason"/> is carried as a <c>reason</c> field in the
/// <c>run.status_changed</c> jsonb payload so the operator's note survives
/// the run timeline.
/// </summary>
/// <param name="db">Scoped orchestration DbContext.</param>
/// <param name="scopeAccessor">Ambient scope — declare system for the run.</param>
/// <param name="clock">Time source for the transition stamp and the journal row.</param>
public sealed class HostCancelRunAdapter(
    OrchestrationDbContext db,
    ISubjectScopeAccessor scopeAccessor,
    TimeProvider clock) : ICancelRunPort
{
    /// <inheritdoc />
    public async Task CancelAsync(RunId runId, string? reason, CancellationToken cancellationToken = default)
    {
        using var systemScope = scopeAccessor.AsSystem("runs-cancel");

        var run = await db.Runs.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken) ?? throw new ProviderNotFoundException(
                "run.not_found",
                $"run '{runId.Value}' not found");
        if (!RunTransitions.IsLegal(run.Status, RunStatus.Cancelled))
        {
            throw new RunDecisionConflictException(
                run.Status,
                RunStatus.Cancelled,
                "cancel");
        }

        var from = run.Status;
        var now = clock.GetUtcNow();
        run.TransitionTo(RunStatus.Cancelled, now);

        var payload = JsonSerializer.Serialize(
            new RunStatusChangedPayload(from.ToString(), RunStatus.Cancelled.ToString(), Actor: "operator", Reason: NormalizeReason(reason)),
            JsonSerializerOptions.Web);

        db.RunEvents.Add(RunEvent.Create(runId, RunEventTypes.RunStatusChanged, payload, now));

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Strips a whitespace-only / null reason to <c>null</c>; the jsonb field is then omitted.</summary>
    /// <param name="reason">User-supplied reason.</param>
    private static string? NormalizeReason(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}

/// <summary>Run status-change payload — shared with engine-internal handlers that emit <c>run.status_changed</c>.</summary>
/// <param name="From">Source status (PascalCase).</param>
/// <param name="To">Target status (PascalCase).</param>
/// <param name="Actor">Operator verb or system consumer name.</param>
/// <param name="Reason">Optional human note (jsonb <c>null</c> when absent).</param>
internal sealed record RunStatusChangedPayload(string From, string To, string Actor, string? Reason);
