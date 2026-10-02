using Comuki.Modules.Procedures.Application.Compiler.Model;
using Comuki.Modules.Procedures.Application.Patches.Diffing;
using Comuki.Modules.Procedures.Application.ProcedureVersions;
using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Comuki.Modules.Procedures.Domain.Ids;
using Comuki.Modules.Procedures.Domain.Patches;
using Comuki.Modules.Procedures.Domain.Patches.Diff;
using Comuki.Modules.Procedures.Domain.Patches.Model;
using NSubstitute;
using Shouldly;
using Xunit;
namespace Comuki.Modules.Procedures.Unit.Patches;

/// <summary>
/// Unit tests for task 3.1: the Application-layer diff service
/// loads the base compiled version from the version store, runs the
/// domain validator, and computes the diff. The unit under test is
/// the orchestration glue; the domain logic itself is covered by
/// <see cref="GraphPatchDiffComputerShould"/> and
/// <see cref="GraphPatchValidatorShould"/>.
/// </summary>
public sealed class GraphPatchDiffServiceShould
{
    [Fact(DisplayName = "Given a patch whose BaseVersionId does not match any compiled version, when the diff is computed, then the refusal is InvalidBaseVersion")]
    public async Task UnknownBaseVersionIsRefusedAsync()
    {
        var store = Substitute.For<IProcedureVersionStore>();
        store.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((CompiledProcedureVersion?)null);
        var service = new GraphPatchDiffService(store);

        var patch = NewPatch();

        var exception = await Should.ThrowAsync<GraphPatchException>(
            async () => await service.ComputeDiffAsync(patch, TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(GraphPatchException.InvalidBaseVersion);
        exception.Message.ShouldContain(patch.BaseVersionId);
    }

    [Fact(DisplayName = "Given a valid base version and a well-formed patch, when the diff is computed, then the diff buckets are populated against the base graph")]
    public async Task WellFormedPatchProducesDiffAgainstBaseAsync()
    {
        var baseGraph = new ProcedureGraph(
            Nodes: [new ProcedureNode("intake", "intake", new Dictionary<string, string>())],
            Edges: []);
        var baseVersion = NewVersion(baseGraph);

        var store = Substitute.For<IProcedureVersionStore>();
        store.GetAsync(baseVersion.VersionId, Arg.Any<CancellationToken>()).Returns(baseVersion);
        var service = new GraphPatchDiffService(store);

        var newNode = new ProcedureNode("verify", "verify", new Dictionary<string, string>());
        var patch = NewPatch(operations: [new GraphPatchOperation.AddNode(newNode)]);

        var diff = await service.ComputeDiffAsync(patch, TestContext.Current.CancellationToken);

        diff.AddedNodes.ShouldHaveSingleItem();
        diff.AddedNodes[0].ShouldBe(newNode);
    }

    [Fact(DisplayName = "Given a patch whose operations reference ids absent from the base, when the diff is computed, then the validator refusal is surfaced (not a generic exception)")]
    public async Task ValidatorRefusalIsSurfacedAsync()
    {
        var baseGraph = new ProcedureGraph(
            Nodes: [new ProcedureNode("intake", "intake", new Dictionary<string, string>())],
            Edges: []);
        var baseVersion = NewVersion(baseGraph);

        var store = Substitute.For<IProcedureVersionStore>();
        store.GetAsync(baseVersion.VersionId, Arg.Any<CancellationToken>()).Returns(baseVersion);
        var service = new GraphPatchDiffService(store);

        var patch = NewPatch(operations: [new GraphPatchOperation.RemoveNode("ghost")]);

        var exception = await Should.ThrowAsync<GraphPatchException>(
            async () => await service.ComputeDiffAsync(patch, TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(GraphPatchException.NodeNotFound);
    }

    private static CompiledProcedureVersion NewVersion(ProcedureGraph graph)
    {
        var graphJson = System.Text.Json.JsonSerializer.Serialize(graph, System.Text.Json.JsonSerializerOptions.Web);
        return new CompiledProcedureVersion(
            VersionId: "v1",
            ProjectId: Guid.NewGuid(),
            ProcedureKey: "test-procedure",
            CatalogVersion: "1.0",
            SourceRef: "refs/heads/main",
            GraphJson: graphJson);
    }

    private static GraphPatch NewPatch(IReadOnlyList<GraphPatchOperation>? operations = null)
    {
        return new GraphPatch(
            Id: GraphPatchId.New(),
            BaseVersionId: "v1",
            ProjectId: Guid.NewGuid(),
            ProcedureKey: "test-procedure",
            Operations: operations ?? [],
            Rationale: "unit test",
            DraftedBy: new GraphPatchDraftedBy("tester", GraphPatchDraftedKind.Operator),
            DraftedAt: DateTimeOffset.UtcNow);
    }
}
