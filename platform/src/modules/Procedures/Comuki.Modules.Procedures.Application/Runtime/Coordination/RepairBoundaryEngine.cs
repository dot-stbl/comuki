namespace Comuki.Modules.Procedures.Application.Runtime.Coordination;

/// <summary>What the repair boundary should do with this outcome.</summary>
public enum RepairAction
{
    /// <summary>Open a new repair generation (semantic retry).</summary>
    OpenNewGeneration,

    /// <summary>Escalate to a human decision — no more self-repair.</summary>
    Escalate,

    /// <summary>Retry execution infrastructure (container died, lease expired) — no semantic generation.</summary>
    RetryExecution,

    /// <summary>Continue to the next node — the boundary passed.</summary>
    Continue,
}

/// <summary>
/// One numbered repair generation: references the evidence from the
/// failing node that triggered it. The materialized execution graph
/// remains acyclic — generations unroll, they never create a back-edge.
/// </summary>
/// <param name="GenerationNumber">1-based ordinal of this generation.</param>
/// <param name="FailedNodeId">The node whose failure opened this generation.</param>
/// <param name="FailingEvidenceRef">Reference to the evidence from the failed node.</param>
/// <param name="OpenedAt">When this generation was opened (UTC).</param>
public sealed record RepairGeneration(
    int GenerationNumber,
    string FailedNodeId,
    string FailingEvidenceRef,
    DateTimeOffset OpenedAt);

/// <summary>
/// The bounded repair-boundary engine: opens numbered generations up to
/// the declared maximum, classifies outcome ports into repair actions,
/// and refuses to loop on inconclusive outcomes. Exhaustion escalates —
/// never loops (spec: "Exceeding the maximum, exhausting the boundary
/// budget, or receiving an inconclusive outcome SHALL move the owning
/// run to escalation").
/// </summary>
public sealed class RepairBoundaryEngine(TimeProvider clock)
{
    /// <summary>
    /// Opens a new repair generation. Throws when the cap is already
    /// reached — the caller escalates to a human instead.
    /// </summary>
    /// <param name="currentGeneration">The current generation number (0 = none yet).</param>
    /// <param name="maxGenerations">The boundary's declared cap.</param>
    /// <param name="failedNodeId">The node whose failure triggered this generation.</param>
    /// <param name="failingEvidenceRef">Evidence from the failed node.</param>
    /// <returns>The newly opened generation.</returns>
    public RepairGeneration OpenGeneration(
        int currentGeneration,
        int maxGenerations,
        string failedNodeId,
        string failingEvidenceRef)
    {
        return currentGeneration >= maxGenerations
            ? throw new ProcedureRuntimeException(
                ProcedureRuntimeException.GenerationsExhausted,
                $"Repair boundary exhausted: generation {currentGeneration} of {maxGenerations}; node '{failedNodeId}' still failing. Escalating.")
            : new RepairGeneration(
                GenerationNumber: currentGeneration + 1,
                FailedNodeId: failedNodeId,
                FailingEvidenceRef: failingEvidenceRef,
                OpenedAt: clock.GetUtcNow());
    }

    /// <summary>
    /// Classifies an outcome port into the action the boundary should take.
    /// An inconclusive or infrastructure-error port never opens a semantic
    /// generation — the spec forbids it ("an inconclusive or infrastructure-
    /// error port never opens a semantic generation").
    /// </summary>
    /// <param name="outcomePort">The outcome port the verify node produced.</param>
    /// <returns>The action to take.</returns>
    public static RepairAction ClassifyOutcome(string outcomePort)
    {
        return outcomePort switch
        {
            "failed" => RepairAction.OpenNewGeneration,
            "inconclusive" => RepairAction.Escalate,
            "infrastructure-error" => RepairAction.RetryExecution,
            _ => RepairAction.Continue,
        };
    }
}
