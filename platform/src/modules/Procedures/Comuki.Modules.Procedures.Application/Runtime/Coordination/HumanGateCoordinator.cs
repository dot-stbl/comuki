namespace Comuki.Modules.Procedures.Application.Runtime.Coordination;

/// <summary>A human gate that is open and waiting for approvals.</summary>
/// <param name="NodeId">Graph-local id of the gate node.</param>
/// <param name="ApprovalFloor">How many distinct human approvals are required.</param>
/// <param name="RequiredApproverIds">Optional allowlist; empty = anyone authorized may approve.</param>
public sealed record GatePending(
    string NodeId,
    int ApprovalFloor,
    IReadOnlyList<string> RequiredApproverIds);

/// <summary>A resolved gate — the decision a person made.</summary>
/// <param name="NodeId">The gate node that was resolved.</param>
/// <param name="Outcome">approved or rejected.</param>
/// <param name="ResolvedBy">The identity of the person who resolved it.</param>
/// <param name="ResolvedAt">When the gate was resolved (UTC).</param>
public sealed record GateResolved(
    string NodeId,
    string Outcome,
    string ResolvedBy,
    DateTimeOffset ResolvedAt);

/// <summary>
/// Coordinates human gates: opens a pending gate, collects approvals,
/// checks quorum (distinct humans — one person approving twice does not
/// count twice), and resolves. The gate blocks the run until quorum is
/// met or the gate expires (spec: "one person approving twice does not
/// satisfy a two-approval gate").
/// </summary>
public sealed class HumanGateCoordinator(TimeProvider clock)
{
    /// <summary>Opens a gate for the given node.</summary>
    /// <param name="nodeId">The gate node's id.</param>
    /// <param name="approvalFloor">How many distinct approvals are required.</param>
    /// <returns>The pending gate.</returns>
    public static GatePending OpenGate(string nodeId, int approvalFloor)
    {
        return new GatePending(nodeId, approvalFloor, []);
    }

    /// <summary>
    /// Checks whether the collected approvals satisfy the gate's quorum.
    /// Counts distinct approver identities — duplicates don't count.
    /// </summary>
    /// <param name="gate">The pending gate.</param>
    /// <param name="approvals">The list of approver identities who have approved.</param>
    /// <returns>True when quorum is met.</returns>
    public static bool CheckQuorum(GatePending gate, IReadOnlyList<string> approvals)
    {
        return approvals.Distinct(StringComparer.Ordinal).Count() >= gate.ApprovalFloor;
    }

    /// <summary>
    /// Resolves a gate with a single decision. For multi-approval gates,
    /// the caller collects approvals first, checks quorum, then resolves.
    /// </summary>
    /// <param name="gate">The pending gate to resolve.</param>
    /// <param name="approverId">The person resolving the gate.</param>
    /// <param name="approved">True = approve, false = reject.</param>
    /// <returns>The resolved gate.</returns>
    public GateResolved ResolveGate(GatePending gate, string approverId, bool approved)
    {
        return gate.RequiredApproverIds.Count > 0
            && !gate.RequiredApproverIds.Contains(approverId, StringComparer.Ordinal)
                ? throw new ProcedureRuntimeException(
                    ProcedureRuntimeException.InconclusiveDoesNotLoop,
                    $"Approver '{approverId}' is not in the required approver list for gate '{gate.NodeId}'.")
                : new GateResolved(
                    gate.NodeId,
                    approved ? "approved" : "rejected",
                    approverId,
                    clock.GetUtcNow());
    }
}
