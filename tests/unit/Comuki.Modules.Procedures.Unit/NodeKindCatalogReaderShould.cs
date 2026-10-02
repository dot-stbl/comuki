using Comuki.Modules.Procedures.Domain.Catalog;
using Comuki.Modules.Procedures.Domain.Exceptions;
using Comuki.Modules.Procedures.Domain.Kinds.Types;
using Comuki.Modules.Procedures.Infrastructure.Loading;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Procedures.Unit;

/// <summary>
/// Unit tests for task 1.1: loads the baseline v1 procedure-node-kind
/// catalog from a temp control-plane tree and asserts the typed descriptor
/// model surfaces the spec scenario for the baseline set (13 kinds, verify
/// has four ports) and refuses a draft missing an owner surface.
/// </summary>
public sealed class NodeKindCatalogReaderShould
{
    private static NodeKindCatalogReader CreateReader()
    {
        return new NodeKindCatalogReader(NullLogger<NodeKindCatalogReader>.Instance);
    }

    [Fact(DisplayName = "Given the baseline v1 markdown set, when LoadAsync runs, then the catalog resolves 13 kinds in alphabetic order")]
    public async Task BaselineCatalogResolves13KindsAsync()
    {
        using var tree = new TempControlPlane();
        tree.Write("intake.md", """
            ---
            key: intake
            title: Intake
            description: Accepts an inbound ticket.
            owner: WorkItem
            parameters: intake-envelope-v1
            ports:
              - accepted
              - rejected
            evidence:
              - intake-record
            risk: low
            idempotency: inherent
            approval_floor: 0
            ---

            First node a procedure runs.
            """);
        tree.Write("classify.md", """
            ---
            key: classify
            title: Classify
            description: Reads the intake and emits a typed classification.
            owner: BrainOperation
            parameters: classify-prompt-v1
            ports:
              - classified
              - ambiguous
            evidence:
              - classification
            risk: low
            idempotency: inherent
            approval_floor: 0
            ---
            """);
        tree.Write("plan.md", """
            ---
            key: plan
            title: Plan
            description: Decomposes an envelope into typed work items.
            owner: BrainOperation
            parameters: plan-prompt-v1
            ports:
              - planned
              - empty
            evidence:
              - work-item-list
            risk: medium
            idempotency: required
            approval_floor: 0
            ---
            """);
        tree.Write("agent.md", """
            ---
            key: agent
            title: Agent
            description: A work item leased to a worker profile.
            owner: WorkItem
            parameters: agent-parameter-schema-v1
            ports:
              - succeeded
              - failed
            evidence:
              - worker-report
            risk: medium
            idempotency: required
            approval_floor: 0
            ---
            """);
        tree.Write("fan-out.md", """
            ---
            key: fan-out
            title: Fan-out
            description: Forks the graph into parallel lanes.
            owner: WorkItem
            parameters: fan-out-config-v1
            ports:
              - dispatched
            evidence:
              - lane-list
            risk: low
            idempotency: inherent
            approval_floor: 0
            ---
            """);
        tree.Write("join.md", """
            ---
            key: join
            title: Join
            description: Synchronizes parallel lanes.
            owner: WorkItem
            parameters: join-config-v1
            ports:
              - all-passed
              - has-failed
            evidence:
              - lane-results
            risk: low
            idempotency: inherent
            approval_floor: 0
            ---
            """);
        tree.Write("verify.md", """
            ---
            key: verify
            title: Verify
            description: Runs a typed verifier against an artifact.
            owner: BrokerOperation
            parameters: verify-call-v1
            ports:
              - passed
              - failed
              - inconclusive
              - infrastructure-error
            evidence:
              - verifier-report
            risk: low
            idempotency: inherent
            approval_floor: 0
            ---
            """);
        tree.Write("review.md", """
            ---
            key: review
            title: Review
            description: A brain review producing structured feedback.
            owner: BrainOperation
            parameters: review-prompt-v1
            ports:
              - approved
              - changes-requested
              - inconclusive
            evidence:
              - review-report
            risk: medium
            idempotency: inherent
            approval_floor: 0
            ---
            """);
        tree.Write("human-gate.md", """
            ---
            key: human-gate
            title: Human gate
            description: Blocks until the approval policy records a decision.
            owner: Decision
            parameters: human-gate-config-v1
            ports:
              - gate.approved
              - gate.rejected
              - gate.expired
            evidence:
              - decision-record
            risk: high
            idempotency: inherent
            approval_floor: 1
            ---
            """);
        tree.Write("repair-boundary.md", """
            ---
            key: repair-boundary
            title: Repair boundary
            description: A bounded re-execution wrapper.
            owner: WorkItem
            parameters: repair-boundary-config-v1
            ports:
              - closed
              - exhausted
              - escalated
            evidence:
              - generation-record
            risk: medium
            idempotency: required
            approval_floor: 0
            ---
            """);
        tree.Write("capability.md", """
            ---
            key: capability
            title: Capability
            description: Routes through the capability broker.
            owner: BrokerOperation
            parameters: capability-call-v1
            ports:
              - succeeded
              - refused
              - failed
            evidence:
              - broker-result
            risk: medium
            idempotency: required
            approval_floor: 0
            ---
            """);
        tree.Write("complete.md", """
            ---
            key: complete
            title: Complete
            description: The terminal success node.
            owner: WorkItem
            parameters: complete-envelope-v1
            ports:
              - closed
            evidence:
              - completion-record
            risk: low
            idempotency: inherent
            approval_floor: 0
            ---
            """);
        tree.Write("escalate.md", """
            ---
            key: escalate
            title: Escalate
            description: Carries the run to a human decision.
            owner: Decision
            parameters: escalate-payload-v1
            ports:
              - escalated
            evidence:
              - escalation-record
            risk: high
            idempotency: inherent
            approval_floor: 0
            ---
            """);

        var catalog = await CreateReader().LoadAsync(tree.Root, TestContext.Current.CancellationToken);

        catalog.Version.ShouldBe(NodeKindCatalog.BaselineVersion);
        catalog.Entries.Select(static entry => entry.Key).ShouldBe(
        [
            "agent",
            "capability",
            "classify",
            "complete",
            "escalate",
            "fan-out",
            "human-gate",
            "intake",
            "join",
            "plan",
            "repair-boundary",
            "review",
            "verify",
        ]);

        var verify = catalog.Find("verify").ShouldNotBeNull().Descriptor;
        verify.OwnerSurface.ShouldBe(NodeKindOwnerSurface.BrokerOperation);
        verify.OutcomePorts
            .Select(static port => port.Name)
            .ShouldBe(["passed", "failed", "inconclusive", "infrastructure-error"]);
        verify.OutcomePorts.Count.ShouldBe(4);
    }

    [Fact(DisplayName = "Given a draft descriptor missing the owner surface, when LoadAsync runs, then it is refused loudly with the kind named")]
    public async Task RefuseDescriptorMissingOwnerAsync()
    {
        using var tree = new TempControlPlane();
        tree.Write("verify.md", """
            ---
            key: verify
            title: Verify
            description: Runs a typed verifier against an artifact.
            parameters: verify-call-v1
            ports:
              - passed
              - failed
              - inconclusive
              - infrastructure-error
            evidence:
              - verifier-report
            risk: low
            idempotency: inherent
            approval_floor: 0
            ---

            No owner: line — must be refused.
            """);

        var exception = await Should.ThrowAsync<ProcedureNodeKindsDomainException>(
            () => CreateReader().LoadAsync(tree.Root, TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(ProcedureNodeKindsDomainException.OwnerSurfaceMissing);
        exception.Message.ShouldContain("verify");
    }

    [Fact(DisplayName = "Given a draft descriptor declaring an unknown owner wire form, when LoadAsync runs, then the catalog refuses it")]
    public async Task RefuseDescriptorWithUnknownOwnerAsync()
    {
        using var tree = new TempControlPlane();
        tree.Write("verify.md", """
            ---
            key: verify
            title: Verify
            description: Runs a typed verifier against an artifact.
            owner: Widget
            parameters: verify-call-v1
            ports:
              - passed
              - failed
              - inconclusive
              - infrastructure-error
            evidence:
              - verifier-report
            risk: low
            idempotency: inherent
            approval_floor: 0
            ---
            """);

        var exception = await Should.ThrowAsync<ProcedureNodeKindsDomainException>(
            () => CreateReader().LoadAsync(tree.Root, TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(ProcedureNodeKindsDomainException.OwnerSurfaceMissing);
        exception.Message.ShouldContain("Widget");
    }

    [Fact(DisplayName = "Given the baseline catalog, when each kind is inspected, then its owner surface is one of the four declared wire forms")]
    public void OwnerSurfacesCoverTheClosedSet()
    {
        var expected = new[]
        {
            NodeKindOwnerSurface.WorkItem,
            NodeKindOwnerSurface.Decision,
            NodeKindOwnerSurface.BrokerOperation,
            NodeKindOwnerSurface.BrainOperation,
        };

        NodeKindOwnerSurface.All.ShouldBe(expected);
    }

    [Fact(DisplayName = "Given a parsed outcome port name, when Define is called with whitespace, then it throws")]
    public void RejectBlankPortNameAsync()
    {
        Should.Throw<ArgumentException>(static () => OutcomePort.Define("   ", role: "should be wired"));
    }
}
