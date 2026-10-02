using Comuki.Modules.Procedures.Application.Compiler;
using Comuki.Modules.Procedures.Application.Compiler.Model;
using Comuki.Modules.Procedures.Application.Patches;
using Comuki.Modules.Procedures.Application.Patches.Events;
using Comuki.Modules.Procedures.Application.Ports;
using Comuki.Modules.Procedures.Application.ProcedureVersions;
using Comuki.Modules.Procedures.Domain.Catalog;
using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Comuki.Modules.Procedures.Domain.Editions;
using Comuki.Modules.Procedures.Domain.Ids;
using Comuki.Modules.Procedures.Domain.Kinds;
using Comuki.Modules.Procedures.Domain.Kinds.Types;
using Comuki.Modules.Procedures.Domain.Layering.Results;
using Comuki.Modules.Procedures.Domain.Patches;
using Comuki.Modules.Procedures.Domain.Patches.Model;
using Comuki.Modules.Procedures.Domain.Patches.Publication;
using Comuki.Shared.Contracts;
using NSubstitute;
using Shouldly;
using Xunit;
namespace Comuki.Modules.Procedures.Unit.Patches;

/// <summary>
/// Unit tests for task 3.2: the publication service refuses brain
/// drafts at the door (spec scenario "Brain drafts a hotfix bypass
/// ... cannot publish it") and refuses same-person approval, then on
/// a valid operator-drafted patch runs the forbidden-surface check,
/// compiles, persists, and writes the outbox event with the from/to
/// version ids.
/// </summary>
public sealed class PublicationServiceShould
{
    private static readonly IReadOnlySet<string> allowedKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "intake", "verify", "human-gate", "repair-boundary",
    };

    [Fact(DisplayName = "Given a brain-drafted patch, when published, then the refusal is brain_draft_not_publishable and the outbox is not called")]
    public async Task BrainDraftIsNotPublishableAsync()
    {
        var (Service, Outbox, VersionStore) = CreateHarness();
        var patch = NewPatch("brain-session-1", GraphPatchDraftedKind.Brain);

        var exception = await Should.ThrowAsync<PublicationException>(
            async () => await Service.PublishAsync(NewRequest(patch), TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(PublicationException.BrainDraftNotPublishable);
        await Outbox.DidNotReceive().PublishAsync(
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an operator-drafted patch where the approver is the same identity, when published, then the refusal is approver_same_as_drafter")]
    public async Task SamePersonCannotApproveOwnDraftAsync()
    {
        var (Service, Outbox, VersionStore) = CreateHarness();
        var patch = NewPatch("alice", GraphPatchDraftedKind.Operator);

        var exception = await Should.ThrowAsync<PublicationException>(
            async () => await Service.PublishAsync(
                NewRequest(patch, approver: "alice"),
                TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(PublicationException.ApproverSameAsDrafter);
    }

    [Fact(DisplayName = "Given an operator-drafted patch with a kind outside the allowlist, when published, then the refusal is kind_not_allowed")]
    public async Task ForbiddenKindRefusedAsync()
    {
        var (Service, Outbox, VersionStore) = CreateHarness();
        var patch = NewPatch("alice", GraphPatchDraftedKind.Operator,
            new GraphPatchOperation.AddNode(new ProcedureNode("rogue", "rogue-kind", new Dictionary<string, string>())));

        var exception = await Should.ThrowAsync<PublicationException>(
            async () => await Service.PublishAsync(NewRequest(patch), TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(PublicationException.KindNotAllowed);
    }

    [Fact(DisplayName = "Given a well-formed operator-drafted patch with a distinct human approver, when published, then a new version is written and the outbox event is published with from/to version ids")]
    public async Task WellFormedPatchPublishesAsync()
    {
        var (Service, Outbox, VersionStore) = CreateHarness();
        var patch = NewPatch("alice", GraphPatchDraftedKind.Operator,
            new GraphPatchOperation.AddNode(new ProcedureNode(
                "verify",
                "verify",
                new Dictionary<string, string> { ["strictness"] = "high" })));

        var result = await Service.PublishAsync(
            NewRequest(patch, approver: "bob"),
            TestContext.Current.CancellationToken);

        result.VersionId.ShouldNotBeNullOrWhiteSpace();
        result.GraphJson.ShouldContain("verify");

        await VersionStore.Received(1).SaveAsync(
            Arg.Is<CompiledProcedureVersion>(version => version.VersionId == result.VersionId),
            Arg.Any<CancellationToken>());

        await Outbox.Received(1).PublishAsync(
            "procedures.procedure.published.v1",
            Arg.Is<ProcedurePublishedV1>(evt =>
                evt.PatchId == patch.Id
                && evt.FromVersionId == patch.BaseVersionId
                && evt.ToVersionId == result.VersionId
                && evt.PublishedBy == "bob"),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a system-drafted patch (automation), when published with a distinct human approver, then the publication succeeds")]
    public async Task SystemDraftIsPublishableAsync()
    {
        var (Service, _, _) = CreateHarness();
        var patch = NewPatch(
            "migration-job-1",
            GraphPatchDraftedKind.System,
            new GraphPatchOperation.AddNode(new ProcedureNode(
                "verify",
                "verify",
                new Dictionary<string, string> { ["strictness"] = "low" })));

        var result = await Service.PublishAsync(
            NewRequest(patch, approver: "ops-team"),
            TestContext.Current.CancellationToken);

        result.VersionId.ShouldNotBeNullOrWhiteSpace();
    }

    private static (PublicationService Service, IOutbox Outbox, IProcedureVersionStore VersionStore) CreateHarness()
    {
        var catalogReader = Substitute.For<INodeKindCatalogReader>();
        catalogReader.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BuildCatalog());
        var editionsSource = Substitute.For<IEditionsFeatureSource>();
        editionsSource.ResolveEffective().Returns(GrantedFeatureKeys.Empty);
        var compiler = new ProcedureCompiler(catalogReader, editionsSource);
        var outbox = Substitute.For<IOutbox>();
        var versionStore = Substitute.For<IProcedureVersionStore>();
        return (new PublicationService(compiler, versionStore, outbox, TimeProvider.System), outbox, versionStore);
    }

    private static NodeKindCatalog BuildCatalog()
    {
        static NodeKindCatalogEntry Entry(string key, NodeKindOwnerSurface owner, params string[] ports)
        {
            return new NodeKindCatalogEntry(key, new NodeKindDescriptor(
                Key: key,
                Title: key,
                Description: $"Test kind {key}.",
                OwnerSurface: owner,
                ParameterSchema: "v1",
                OutcomePorts: [.. ports.Select(static port => OutcomePort.Define(port, $"Port {port}."))],
                EvidenceRequirements: ["report"],
                RiskClass: "low",
                Idempotency: NodeKindIdempotency.Inherent,
                ApprovalFloor: 0,
                EditionsFeatureKey: null));
        }

        var entries = new List<NodeKindCatalogEntry>
        {
            Entry("intake", NodeKindOwnerSurface.WorkItem, "accepted", "rejected"),
            Entry("verify", NodeKindOwnerSurface.BrokerOperation, "passed", "failed", "inconclusive"),
            Entry("human-gate", NodeKindOwnerSurface.Decision, "gate.approved", "gate.rejected"),
            Entry("repair-boundary", NodeKindOwnerSurface.BrokerOperation, "boundary.exhausted"),
        };
        return new NodeKindCatalog("1.0", "test", entries);
    }

    private static GraphPatch NewPatch(
        string draftedByIdentity,
        GraphPatchDraftedKind kind,
        params GraphPatchOperation[] operations)
    {
        return new GraphPatch(
            Id: GraphPatchId.New(),
            BaseVersionId: "base-v1",
            ProjectId: Guid.NewGuid(),
            ProcedureKey: "test-procedure",
            Operations: operations,
            Rationale: "unit test",
            DraftedBy: new GraphPatchDraftedBy(draftedByIdentity, kind),
            DraftedAt: DateTimeOffset.UtcNow);
    }

    private static PublicationRequest NewRequest(GraphPatch patch, string approver = "bob")
    {
        var baseGraph = new ProcedureGraph(
            Nodes: [new ProcedureNode("intake", "intake", new Dictionary<string, string>())],
            Edges: []);
        var layered = new LayeredProcedure(
            ProcedureName: "test-procedure",
            Nodes: baseGraph.Nodes,
            Edges: baseGraph.Edges,
            AllowedKindKeys: allowedKinds,
            MaxGenerations: 5,
            MinApprovals: 1,
            RepositoryBindings: []);
        return new PublicationRequest(
            Patch: patch,
            Approver: approver,
            Context: new PublicationContext(allowedKinds, MaxGenerations: 5, MinApprovals: 1),
            LayeredProcedure: layered);
    }
}
