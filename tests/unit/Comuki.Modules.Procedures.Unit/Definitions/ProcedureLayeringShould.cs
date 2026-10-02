using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Comuki.Modules.Procedures.Domain.Layering;
using Comuki.Modules.Procedures.Domain.Layering.Model;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Procedures.Unit.Definitions;

/// <summary>
/// Unit tests for task 2.2: structural layered-procedure merge per
/// design decision 2. Three scenarios cover the spec scenarios:
/// <list type="number">
///   <item>Layering merge produces a layered procedure with the
///   project's graph, the merged allowlist, the merged floors, and
///   the bindings attached.</item>
///   <item>Conflicts are loud refusals that name the offending
///   element (spec scenario: "conflict throws with node named").</item>
///   <item>Repository bindings cannot introduce control flow — a
///   binding with non-empty <see cref="RepositoryBinding.AdditionalNodes"/>
///   or <see cref="RepositoryBinding.AdditionalEdges"/> is refused
///   (spec requirement: "repository binding SHALL NOT introduce
///   control flow").</item>
/// </list>
/// </summary>
public sealed class ProcedureLayeringShould
{
    /// <summary>The platform's seed allowlist — mirrors the baseline v1 catalog from task 1.1.</summary>
    private static readonly IReadOnlySet<string> platformAllowed =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "intake", "classify", "plan", "agent", "fan-out", "join",
            "verify", "review", "human-gate", "repair-boundary",
            "capability", "complete", "escalate",
        };

    private static PlatformDefaults PlatformDefaults()
    {
        return new(AllowedKindKeys: platformAllowed, MaxGenerations: 5, MinApprovals: 1);
    }

    private static ProcedureDefinition DefinitionWithSingleNode()
    {
        return new(
            Name: "checkout-flow",
            Nodes: [new ProcedureNode("start", "intake", new Dictionary<string, string>())],
            Edges: []);
    }

    private static ProcedureNode MakeNode(string id, string kindKey)
    {
        return new(id, kindKey, new Dictionary<string, string>());
    }

    [Fact(DisplayName = "Given platform defaults and a project policy, when merged, then the layered procedure carries the project's graph, the merged allowlist, and the merged floors")]
    public void MergeProducesExpectedLayeredProcedure()
    {
        var platform = PlatformDefaults();
        var projectNode = MakeNode("start", "intake");
        var additionalKind = "custom-kind";
        var platformWithExtra = platform with { };    // record copy keeps baseline
        var policy = new ProjectPolicy(
            Definition: new ProcedureDefinition(
                Name: "checkout-flow",
                Nodes: [projectNode],
                Edges: []),
            AdditionalAllowedKindKeys: new HashSet<string>(StringComparer.Ordinal) { additionalKind },
            MaxGenerationsOverride: 3,
            MinApprovalsOverride: 2);

        var layered = ProcedureLayering.Merge(
            platform: platformWithExtra,
            project: policy,
            repositoryBindings: []);

        layered.ProcedureName.ShouldBe("checkout-flow");
        layered.Nodes.Count.ShouldBe(1);
        layered.Nodes[0].Id.ShouldBe("start");
        layered.Edges.ShouldBeEmpty();
        layered.AllowedKindKeys.ShouldContain("intake");
        layered.AllowedKindKeys.ShouldContain(additionalKind);
        layered.MaxGenerations.ShouldBe(3);
        layered.MinApprovals.ShouldBe(2);
        layered.RepositoryBindings.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a project with a duplicate node id, when layered, then the merge refuses loudly and names the offending element")]
    public void ConflictWithDuplicateNodeIdIsRefusedWithName()
    {
        var platform = PlatformDefaults();
        var policy = new ProjectPolicy(
            Definition: new ProcedureDefinition(
                Name: "checkout-flow",
                Nodes: [
                    MakeNode("start", "intake"),
                    MakeNode("start", "classify"),  // duplicate id
                ],
                Edges: []),
            AdditionalAllowedKindKeys: null,
            MaxGenerationsOverride: null,
            MinApprovalsOverride: null);

        var exception = Should.Throw<ProcedureLayeringException>(
            () => ProcedureLayering.Merge(platform, policy, repositoryBindings: null));

        exception.Code.ShouldBe(ProcedureLayeringException.DuplicateNodeId);
        exception.Message.ShouldContain("start");
        exception.Message.ShouldContain("classify");
    }

    [Fact(DisplayName = "Given a project whose node references a kind absent from the platform allowlist, when layered, then the merge refuses and names the kind")]
    public void KindNotAllowedIsRefusedWithName()
    {
        var platform = PlatformDefaults();
        var policy = new ProjectPolicy(
            Definition: new ProcedureDefinition(
                Name: "checkout-flow",
                Nodes: [MakeNode("start", "custom-undeclared")],
                Edges: []),
            AdditionalAllowedKindKeys: null,
            MaxGenerationsOverride: null,
            MinApprovalsOverride: null);

        var exception = Should.Throw<ProcedureLayeringException>(
            () => ProcedureLayering.Merge(platform, policy, repositoryBindings: null));

        exception.Code.ShouldBe(ProcedureLayeringException.KindNotAllowed);
        exception.Message.ShouldContain("start");
        exception.Message.ShouldContain("custom-undeclared");
    }

    [Fact(DisplayName = "Given a project that loosens the platform's MaxGenerations floor, when layered, then the merge refuses with the floor value named")]
    public void ProjectLooseningMaxGenerationsIsRefused()
    {
        var platform = PlatformDefaults();  // MaxGenerations = 5
        var policy = new ProjectPolicy(
            Definition: DefinitionWithSingleNode(),
            AdditionalAllowedKindKeys: null,
            MaxGenerationsOverride: 7,    // loosens the floor
            MinApprovalsOverride: null);

        var exception = Should.Throw<ProcedureLayeringException>(
            () => ProcedureLayering.Merge(platform, policy, repositoryBindings: null));

        exception.Code.ShouldBe(ProcedureLayeringException.RepositoryBindingAddsControlFlow);
        exception.Message.ShouldContain("MaxGenerationsOverride");
    }

    [Fact(DisplayName = "Given a repository binding that tries to add a node, when layered, then the merge refuses with the binding repository id named")]
    public void RepositoryBindingCannotAddNodes()
    {
        var platform = PlatformDefaults();
        var policy = new ProjectPolicy(
            Definition: DefinitionWithSingleNode(),
            AdditionalAllowedKindKeys: null,
            MaxGenerationsOverride: null,
            MinApprovalsOverride: null);

        var badBinding = new RepositoryBinding(
            RepositoryId: "repo-001",
            ProcedureName: "checkout-flow",
            PinnedCatalogRefs: new HashSet<string>(StringComparer.Ordinal) { "git:abc@123" },
            AdditionalNodes: [MakeNode("stowaway", "verify")],
            AdditionalEdges: null);

        var exception = Should.Throw<ProcedureLayeringException>(
            () => ProcedureLayering.Merge(platform, policy, [badBinding]));

        exception.Code.ShouldBe(ProcedureLayeringException.RepositoryBindingAddsControlFlow);
        exception.Message.ShouldContain("repo-001");
    }

    [Fact(DisplayName = "Given a repository binding that tries to add an edge, when layered, then the merge refuses with the binding repository id named")]
    public void RepositoryBindingCannotAddEdges()
    {
        var platform = PlatformDefaults();
        var policy = new ProjectPolicy(
            Definition: DefinitionWithSingleNode(),
            AdditionalAllowedKindKeys: null,
            MaxGenerationsOverride: null,
            MinApprovalsOverride: null);

        var badBinding = new RepositoryBinding(
            RepositoryId: "repo-002",
            ProcedureName: "checkout-flow",
            PinnedCatalogRefs: new HashSet<string>(StringComparer.Ordinal) { "git:abc@123" },
            AdditionalNodes: null,
            AdditionalEdges: [
                new ProcedureEdge(FromNodeId: "start", FromPort: "accepted", ToNodeId: "stowaway"),
            ]);

        var exception = Should.Throw<ProcedureLayeringException>(
            () => ProcedureLayering.Merge(platform, policy, [badBinding]));

        exception.Code.ShouldBe(ProcedureLayeringException.RepositoryBindingAddsControlFlow);
        exception.Message.ShouldContain("repo-002");
    }

    [Fact(DisplayName = "Given an edge that references an unknown source node id, when layered, then the merge refuses with the edge's source id named")]
    public void EdgeWithUnknownSourceIsRefused()
    {
        var platform = PlatformDefaults();
        var policy = new ProjectPolicy(
            Definition: new ProcedureDefinition(
                Name: "checkout-flow",
                Nodes: [MakeNode("start", "intake")],
                Edges: [
                    new ProcedureEdge(FromNodeId: "ghost", FromPort: "accepted", ToNodeId: "start"),
                ]),
            AdditionalAllowedKindKeys: null,
            MaxGenerationsOverride: null,
            MinApprovalsOverride: null);

        var exception = Should.Throw<ProcedureLayeringException>(
            () => ProcedureLayering.Merge(platform, policy, repositoryBindings: null));

        exception.Code.ShouldBe(ProcedureLayeringException.EdgeTargetsUnknownNode);
        exception.Message.ShouldContain("ghost");
    }

    [Fact(DisplayName = "Given several repository bindings all selecting the same procedure, when layered, then one procedure is published against several repositories (spec scenario: one procedure serves several repositories)")]
    public void OneProcedureMayServeSeveralRepositories()
    {
        var platform = PlatformDefaults();
        var policy = new ProjectPolicy(
            Definition: new ProcedureDefinition(
                Name: "checkout-flow",
                Nodes: [MakeNode("start", "intake")],
                Edges: []),
            AdditionalAllowedKindKeys: null,
            MaxGenerationsOverride: null,
            MinApprovalsOverride: null);

        var first = new RepositoryBinding(
            RepositoryId: "repo-A",
            ProcedureName: "checkout-flow",
            PinnedCatalogRefs: new HashSet<string>(StringComparer.Ordinal) { "git:abc@123" });
        var second = new RepositoryBinding(
            RepositoryId: "repo-B",
            ProcedureName: "checkout-flow",
            PinnedCatalogRefs: new HashSet<string>(StringComparer.Ordinal) { "git:def@456" });

        var layered = ProcedureLayering.Merge(platform, policy, [first, second]);

        layered.RepositoryBindings.Count.ShouldBe(2);
        layered.RepositoryBindings.Select(static binding => binding.RepositoryId)
            .ShouldBe(["repo-A", "repo-B"]);
        layered.ProcedureName.ShouldBe("checkout-flow");
    }
}
