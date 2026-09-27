using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Host.Runs;

/// <summary>
/// A decision endpoint was called on a run whose current status disallows
/// the requested transition (e.g. approving a <c>Succeeded</c> run,
/// cancelling a <c>Cancelled</c> one) — a state-machine conflict answered
/// by the composition-root handler as HTTP 409 with the current status and
/// the decision verb riding along.
/// </summary>
/// <remarks>Builds a 409-mapped conflict for a single run-status decision.</remarks>
/// <param name="current">Run's current status (PascalCase).</param>
/// <param name="requested">Status the decision tried to land on.</param>
/// <param name="decision">Operator verb (e.g. <c>approve</c>, <c>cancel</c>).</param>
public sealed class RunDecisionConflictException(Engine.Orchestration.Domain.RunStatus current, Engine.Orchestration.Domain.RunStatus requested, string decision) : DomainException(ErrorCode, $"run in {current} cannot be {decision}d (would land on {requested})")
{
    private const string ErrorCode = "run.terminal_state";

    /// <summary>Current run status (PascalCase).</summary>
    public Engine.Orchestration.Domain.RunStatus Current { get; } = current;

    /// <summary>Status the decision tried to land on (PascalCase).</summary>
    public Engine.Orchestration.Domain.RunStatus Requested { get; } = requested;

    /// <summary>Operator verb (e.g. <c>approve</c>, <c>cancel</c>).</summary>
    public string Decision { get; } = decision;
}
