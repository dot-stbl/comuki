using Comuki.Modules.Work.Domain.Ids;

namespace Comuki.Modules.Work.Application.Decisions;

/// <summary>
/// <c>Work.Decide</c> command — the umbrella's task 5.6 invariant:
/// every Decision reaches the aggregate through this single entry
/// point; an LLM path never holds a token that calls the underlying
/// state transition directly. The handler dispatches through the
/// <see cref="IWorkDecisionKindHandler"/> registry: each
/// <see cref="Domain.Decisions.DecisionKind"/> maps to one
/// handler, DI supplies the closed set, the dispatcher picks by
/// <see cref="Domain.Decisions.Decision.Kind"/>.
/// </summary>
/// <param name="TaskId">The Task the Decision is being applied to.</param>
/// <param name="Decision">The Decision to apply (kind + payload); see <see cref="Domain.Decisions.Decision"/>.</param>
public sealed record WorkDecideCommand(WorkTaskId TaskId, Domain.Decisions.Decision Decision);

/// <summary>
/// One seam on the per-kind branch —
/// <c>Retry</c>/<c>Replacement</c>/<c>Waiver</c>/<c>FailedResolution</c>/<c>Cancellation</c>.
/// The closed set of implementations is the registry
/// <c>WorkDecideHandler</c> iterates over; the kind on the
/// <see cref="Domain.Decisions.Decision"/> picks the branch. The
/// <c>internal</c> modifier on <see cref="Domain.Decisions.DecisionKind"/>
/// keeps accidental per-subscriber registrations out (the open
/// marker is the kind's addition, not a code change in the
/// dispatcher).
/// </summary>
public interface IWorkDecisionKindHandler
{
    /// <summary>The kind this handler responds to.</summary>
    public Domain.Decisions.DecisionKind Kind { get; }

    /// <summary>Apply the decision to the Task; persist + publish the matching event.</summary>
    /// <param name="command">The command being handled (carries the <see cref="WorkDecideCommand.TaskId"/> and <see cref="WorkDecideCommand.Decision"/>).</param>
    /// <param name="cancellationToken">Cancellation token; cancelled requests surface as <see cref="OperationCanceledException"/>.</param>
    /// <returns>A task that completes when the decision has been persisted and the matching outbox event published.</returns>
    public Task HandleAsync(WorkDecideCommand command, CancellationToken cancellationToken);
}
