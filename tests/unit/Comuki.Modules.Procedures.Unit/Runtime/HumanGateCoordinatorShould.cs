using Comuki.Modules.Procedures.Application.Runtime;
using Comuki.Modules.Procedures.Application.Runtime.Coordination;
using Shouldly;
using Xunit;
namespace Comuki.Modules.Procedures.Unit.Runtime;

/// <summary>
/// Unit tests for task 5.2: the human gate coordinator opens gates,
/// checks quorum with distinct-human counting (one person approving
/// twice does not satisfy a two-approval gate), and resolves.
/// </summary>
public sealed class HumanGateCoordinatorShould
{
    [Fact(DisplayName = "Given a gate with floor 1, when opened, then the pending gate carries the node id and floor")]
    public void GateOpensWithNodeIdAndFloor()
    {
        var gate = HumanGateCoordinator.OpenGate("approval-checkpoint", 1);

        gate.NodeId.ShouldBe("approval-checkpoint");
        gate.ApprovalFloor.ShouldBe(1);
        gate.RequiredApproverIds.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given floor 2 and two distinct approvals, when quorum is checked, then it is satisfied")]
    public void TwoDistinctApproversSatisfyQuorumOfTwo()
    {
        var gate = HumanGateCoordinator.OpenGate("deploy-gate", 2);

        HumanGateCoordinator.CheckQuorum(gate, ["alice", "bob"]).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given floor 2 and one person approving twice, when quorum is checked, then it is NOT satisfied")]
    public void OnePersonApprovingTwiceDoesNotSatisfyQuorumOfTwo()
    {
        var gate = HumanGateCoordinator.OpenGate("deploy-gate", 2);

        HumanGateCoordinator.CheckQuorum(gate, ["alice", "alice"]).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given floor 1 and one approval, when quorum is checked, then it is satisfied")]
    public void OneApprovalSatisfiesQuorumOfOne()
    {
        var gate = HumanGateCoordinator.OpenGate("review-gate", 1);

        HumanGateCoordinator.CheckQuorum(gate, ["bob"]).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given an authorized approver, when ResolveGate runs with approved=true, then the outcome is approved")]
    public void AuthorizedApproverResolvesWithApproval()
    {
        var clock = TimeProvider.System;
        var coordinator = new HumanGateCoordinator(clock);
        var gate = HumanGateCoordinator.OpenGate("deploy-gate", 1);

        var resolved = coordinator.ResolveGate(gate, "alice", approved: true);

        resolved.NodeId.ShouldBe("deploy-gate");
        resolved.Outcome.ShouldBe("approved");
        resolved.ResolvedBy.ShouldBe("alice");
        resolved.ResolvedAt.ShouldNotBe(default);
    }

    [Fact(DisplayName = "Given an authorized approver, when ResolveGate runs with approved=false, then the outcome is rejected")]
    public void AuthorizedApproverResolvesWithRejection()
    {
        var clock = TimeProvider.System;
        var coordinator = new HumanGateCoordinator(clock);
        var gate = HumanGateCoordinator.OpenGate("deploy-gate", 1);

        var resolved = coordinator.ResolveGate(gate, "alice", approved: false);

        resolved.Outcome.ShouldBe("rejected");
    }

    [Fact(DisplayName = "Given a gate with a required-approver allowlist, when a non-listed approver tries to resolve, then it throws")]
    public void NonListedApproverIsRefused()
    {
        var clock = TimeProvider.System;
        var coordinator = new HumanGateCoordinator(clock);
        var gate = new GatePending("secure-gate", 1, ["carol", "dave"]);

        var exception = Should.Throw<ProcedureRuntimeException>(
            () => coordinator.ResolveGate(gate, "eve", approved: true));

        exception.Message.ShouldContain("eve");
        exception.Message.ShouldContain("secure-gate");
    }
}
