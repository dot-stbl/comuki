namespace Comuki.Host.Runs;

/// <summary>
/// Local host-side exception the run controllers catch and translate to
/// HTTP 409 ProblemDetails: a decision endpoint was called on a run whose
/// current status disallows the requested transition (e.g. approving a
/// <c>Succeeded</c> run, cancelling a <c>Cancelled</c> one). Mirrors
/// <c>ChatApprovePendingException</c> for the chat surface — the central
/// <c>ProviderExceptionHandler</c> cannot map it because the underlying
/// problem is a state-machine conflict, not an upstream / semantic fault.
/// </summary>
/// <remarks>Builds a 409-mapped conflict for a single run-status decision.</remarks>
/// <param name="current">Run's current status (PascalCase).</param>
/// <param name="requested">Status the decision tried to land on.</param>
/// <param name="decision">Operator verb (e.g. <c>approve</c>, <c>cancel</c>).</param>
public sealed class RunDecisionConflictException(Engine.Orchestration.Domain.RunStatus current, Engine.Orchestration.Domain.RunStatus requested, string decision) : Exception($"run in {current} cannot be {decision}d (would land on {requested})")
{

    /// <summary>Current run status (PascalCase).</summary>
    public Engine.Orchestration.Domain.RunStatus Current { get; } = current;

    /// <summary>Status the decision tried to land on (PascalCase).</summary>
    public Engine.Orchestration.Domain.RunStatus Requested { get; } = requested;

    /// <summary>Operator verb (e.g. <c>approve</c>, <c>cancel</c>).</summary>
    public string Decision { get; } = decision;
}
