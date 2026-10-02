using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Comuki.Modules.Procedures.Domain.Ids;
using Comuki.Modules.Procedures.Domain.Patches;
using Comuki.Modules.Procedures.Domain.Patches.Model;
using Comuki.Modules.Procedures.Domain.Patches.Publication;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Procedures.Unit.Patches;

/// <summary>
/// Unit tests for task 3.2: the publication surface validator refuses
/// a patch that tries to widen its own surface — a kind key the
/// procedure's allowlist does not grant, a human-gate whose
/// <c>approvals</c> is below the autonomy floor, or a repair-boundary
/// whose <c>max-generations</c> is above the budget ceiling. The
/// validator runs before the compile gate (design decision 5: "patches
/// touching publish rights, autonomy ceilings, or budget maxima are
/// refused before compile").
/// </summary>
public sealed class PublicationSurfaceValidatorShould
{
    private static readonly IReadOnlySet<string> allowedKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "intake", "verify", "human-gate", "repair-boundary",
    };

    [Fact(DisplayName = "Given an AddNode whose kind key is absent from the allowlist, when validated, then the refusal names the kind")]
    public void KindNotAllowedIsRefused()
    {
        var baseGraph = NewGraph();
        var patch = NewPatch(new GraphPatchOperation.AddNode(new ProcedureNode("rogue", "rogue-kind", new Dictionary<string, string>())));

        var exception = Should.Throw<PublicationException>(
            () => PublicationSurfaceValidator.Validate(
                baseGraph,
                patch,
                new PublicationContext(allowedKinds, MaxGenerations: 5, MinApprovals: 1)));

        exception.Code.ShouldBe(PublicationException.KindNotAllowed);
        exception.Message.ShouldContain("rogue-kind");
    }

    [Fact(DisplayName = "Given a human-gate AddNode with approvals below the floor, when validated, then the refusal names the autonomy floor")]
    public void AutonomyBelowFloorIsRefused()
    {
        var baseGraph = NewGraph();
        var patch = NewPatch(new GraphPatchOperation.AddNode(new ProcedureNode(
            "gate",
            "human-gate",
            new Dictionary<string, string> { ["approvals"] = "0" })));

        var exception = Should.Throw<PublicationException>(
            () => PublicationSurfaceValidator.Validate(
                baseGraph,
                patch,
                new PublicationContext(allowedKinds, MaxGenerations: 5, MinApprovals: 1)));

        exception.Code.ShouldBe(PublicationException.AutonomyBelowFloor);
        exception.Message.ShouldContain("MinApprovals");
    }

    [Fact(DisplayName = "Given a repair-boundary AddNode with max-generations above the floor, when validated, then the refusal names the budget ceiling")]
    public void BudgetAboveFloorIsRefused()
    {
        var baseGraph = NewGraph();
        var patch = NewPatch(new GraphPatchOperation.AddNode(new ProcedureNode(
            "loop",
            "repair-boundary",
            new Dictionary<string, string> { ["max-generations"] = "99" })));

        var exception = Should.Throw<PublicationException>(
            () => PublicationSurfaceValidator.Validate(
                baseGraph,
                patch,
                new PublicationContext(allowedKinds, MaxGenerations: 5, MinApprovals: 1)));

        exception.Code.ShouldBe(PublicationException.BudgetAboveFloor);
        exception.Message.ShouldContain("MaxGenerations");
    }

    [Fact(DisplayName = "Given a human-gate AddNode without the approvals parameter, when validated, then the refusal names the missing parameter")]
    public void MissingPolicyParameterIsRefused()
    {
        var baseGraph = NewGraph();
        var patch = NewPatch(new GraphPatchOperation.AddNode(new ProcedureNode(
            "gate",
            "human-gate",
            new Dictionary<string, string> { ["x"] = "0" })));

        var exception = Should.Throw<PublicationException>(
            () => PublicationSurfaceValidator.Validate(
                baseGraph,
                patch,
                new PublicationContext(allowedKinds, MaxGenerations: 5, MinApprovals: 1)));

        exception.Code.ShouldBe(PublicationException.PolicyParameterMissing);
        exception.Message.ShouldContain("approvals");
    }

    [Fact(DisplayName = "Given a ReParameterizeNode on a human-gate that lowers approvals below the floor, when validated, then the refusal is autonomy_below_floor")]
    public void ReParameterizeHumanGateBelowFloorIsRefused()
    {
        var baseGraph = NewGraph(("gate", "human-gate"));

        var patch = NewPatch(new GraphPatchOperation.ReParameterizeNode(
            "gate",
            new Dictionary<string, string> { ["approvals"] = "0" }));

        var exception = Should.Throw<PublicationException>(
            () => PublicationSurfaceValidator.Validate(
                baseGraph,
                patch,
                new PublicationContext(allowedKinds, MaxGenerations: 5, MinApprovals: 1)));

        exception.Code.ShouldBe(PublicationException.AutonomyBelowFloor);
    }

    [Fact(DisplayName = "Given a well-formed patch that respects every floor, when validated, then no exception is raised")]
    public void WellFormedPatchIsAccepted()
    {
        var baseGraph = NewGraph(("intake", "intake"));
        var patch = NewPatch(
            new GraphPatchOperation.AddNode(new ProcedureNode(
                "gate",
                "human-gate",
                new Dictionary<string, string> { ["approvals"] = "2" })),
            new GraphPatchOperation.AddNode(new ProcedureNode(
                "loop",
                "repair-boundary",
                new Dictionary<string, string> { ["max-generations"] = "3" })));

        Should.NotThrow(() => PublicationSurfaceValidator.Validate(
            baseGraph,
            patch,
            new PublicationContext(allowedKinds, MaxGenerations: 5, MinApprovals: 1)));
    }

    private static ProcedureGraph NewGraph(params (string Id, string KindKey)[] nodes)
    {
        var nodeList = nodes
            .Select(static spec => new ProcedureNode(spec.Id, spec.KindKey, new Dictionary<string, string>()))
            .ToList();
        return new ProcedureGraph(nodeList, []);
    }

    private static GraphPatch NewPatch(params GraphPatchOperation[] operations)
    {
        return new GraphPatch(
            Id: GraphPatchId.New(),
            BaseVersionId: "base-version",
            ProjectId: Guid.NewGuid(),
            ProcedureKey: "test-procedure",
            Operations: operations,
            Rationale: "unit test",
            DraftedBy: new GraphPatchDraftedBy("tester", GraphPatchDraftedKind.Operator),
            DraftedAt: DateTimeOffset.UtcNow);
    }
}
