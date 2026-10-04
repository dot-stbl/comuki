using Comuki.Modules.Procedures.Application;
using Comuki.Modules.Procedures.Application.Compiler;
using Comuki.Modules.Procedures.Application.Compiler.Model;
using Comuki.Modules.Procedures.Application.Ports;
using Comuki.Modules.Procedures.Domain.Catalog;
using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Comuki.Modules.Procedures.Domain.Editions;
using Comuki.Modules.Procedures.Domain.Kinds;
using Comuki.Modules.Procedures.Domain.Kinds.Types;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;
using AppProcedureDefinition = Comuki.Modules.Procedures.Application.Compiler.Model.ProcedureDefinition;
namespace Comuki.Modules.Procedures.Unit.Compiler;

/// <summary>
/// Unit tests for task 2.3: the deterministic compile gate. The compiler
/// validates the procedure graph against the pinned catalog and produces
/// a content-addressed version — identical inputs always produce an
/// identical version id.
/// </summary>
public sealed class ProcedureCompilerShould
{
    [Fact(DisplayName = "Given a valid procedure, when compiled twice, then the version id is deterministic")]
    public async Task CompileIsDeterministicAsync()
    {
        var compiler = CreateCompiler();
        var definition = ValidDefinition();

        var first = await compiler.CompileAsync(definition, TestContext.Current.CancellationToken);
        var second = await compiler.CompileAsync(definition, TestContext.Current.CancellationToken);

        first.VersionId.ShouldBe(second.VersionId);
        first.VersionId.ShouldNotBeNullOrWhiteSpace();
        first.CatalogVersion.ShouldBe("1.0");
        first.SourceRef.ShouldBe(definition.GitRef);
    }

    [Fact(DisplayName = "Given a graph with a cycle, when compiled, then it is refused with the cycle path named")]
    public async Task RefuseCycleAsync()
    {
        var compiler = CreateCompiler();
        var definition = DefinitionWithCycle();

        var exception = await Should.ThrowAsync<ProcedureCompilerException>(
            async () => await compiler.CompileAsync(definition, TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(ProcedureCompilerException.CycleDetected);
        exception.Message.ShouldContain("a → b → a");
    }

    [Fact(DisplayName = "Given an edge to a port the kind does not declare, when compiled, then it is refused with the node and port named")]
    public async Task RefuseUnknownPortAsync()
    {
        var compiler = CreateCompiler();
        var definition = DefinitionWithUnknownPort();

        var exception = await Should.ThrowAsync<ProcedureCompilerException>(
            async () => await compiler.CompileAsync(definition, TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(ProcedureCompilerException.PortNotFound);
        exception.Message.ShouldContain("start");
        exception.Message.ShouldContain("nonexistent");
    }

    [Fact(DisplayName = "Given a node referencing an unknown kind, when compiled, then it is refused")]
    public async Task RefuseUnknownKindAsync()
    {
        var compiler = CreateCompiler();
        var definition = DefinitionWithUnknownKind();

        var exception = await Should.ThrowAsync<ProcedureCompilerException>(
            async () => await compiler.CompileAsync(definition, TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(ProcedureCompilerException.PortNotFound);
        exception.Message.ShouldContain("unknown-kind");
    }

    [Fact(DisplayName = "Given an absolute control-plane root, when the compiler runs, then the catalog reader is called with that absolute path")]
    public async Task PassesAbsoluteControlPlaneRootToCatalogReaderAsync()
    {
        // Spec context: the host runs from a bin directory, so a relative
        // 'control-plane/' lookup misses the folder. The compile gate reads
        // the absolute path from IOptions and passes it through verbatim
        // to the catalog reader. We assert the path is absolute and equal
        // to the bound value — a hard contract the prior compile-gate
        // hardcoded "control-plane" string violated.
        var absoluteRoot = Path.Combine(
            Path.GetTempPath(),
            $"comuki-procedures-abs-{Guid.NewGuid():N}");
        var catalogReader = Substitute.For<INodeKindCatalogReader>();
        catalogReader.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CreateCatalog());
        var editionsSource = Substitute.For<IEditionsFeatureSource>();
        editionsSource.ResolveEffective().Returns(GrantedFeatureKeys.Empty);
        var options = Options.Create(new ProceduresOptions
        {
            ControlPlaneRoot = absoluteRoot,
        });
        var compiler = new ProcedureCompiler(catalogReader, editionsSource, options);

        await compiler.CompileAsync(ValidDefinition(), TestContext.Current.CancellationToken);

        await catalogReader.Received(1).LoadAsync(
            Arg.Is<string>(path => path == absoluteRoot && Path.IsPathRooted(path)),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an empty control-plane root, when the compiler runs, then it refuses loudly before touching the catalog reader")]
    public async Task RefuseEmptyControlPlaneRootAsync()
    {
        var catalogReader = Substitute.For<INodeKindCatalogReader>();
        var editionsSource = Substitute.For<IEditionsFeatureSource>();
        editionsSource.ResolveEffective().Returns(GrantedFeatureKeys.Empty);
        var options = Options.Create(new ProceduresOptions { ControlPlaneRoot = string.Empty });
        var compiler = new ProcedureCompiler(catalogReader, editionsSource, options);

        var exception = await Should.ThrowAsync<ProcedureCompilerException>(
            async () => await compiler.CompileAsync(ValidDefinition(), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("ControlPlaneRoot");
        await catalogReader.DidNotReceiveWithAnyArgs().LoadAsync(null!, TestContext.Current.CancellationToken);
    }

    private static ProcedureCompiler CreateCompiler()
    {
        var catalogReader = Substitute.For<INodeKindCatalogReader>();
        catalogReader.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CreateCatalog());
        var editionsSource = Substitute.For<IEditionsFeatureSource>();
        editionsSource.ResolveEffective().Returns(GrantedFeatureKeys.Empty);
        // The absolute path is what the host binds in production; the
        // compiler passes it through to the catalog reader, so any
        // absolute path is a fair substitute here. The test asserts on
        // the value the reader is called with — see the explicit
        // assertion on the absolute-path shape in the suite below.
        var options = Options.Create(new ProceduresOptions
        {
            ControlPlaneRoot = Path.Combine(Path.GetTempPath(), $"comuki-procedures-test-{Guid.NewGuid():N}"),
        });
        return new ProcedureCompiler(catalogReader, editionsSource, options);
    }

    private static NodeKindCatalog CreateCatalog()
    {
        var entries = new List<NodeKindCatalogEntry>
        {
            Entry("intake", NodeKindOwnerSurface.WorkItem, "accepted", "rejected"),
            Entry("verify", NodeKindOwnerSurface.BrokerOperation, "passed", "failed", "inconclusive", "infrastructure-error"),
        };
        return new NodeKindCatalog("1.0", "test", entries);
    }

    private static NodeKindCatalogEntry Entry(string key, NodeKindOwnerSurface owner, params string[] ports)
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

    private static AppProcedureDefinition ValidDefinition()
    {
        return new AppProcedureDefinition(
            ProjectId: Guid.NewGuid(),
            ProcedureKey: "test-procedure",
            GitRef: "refs/heads/main",
            Graph: new ProcedureGraph(
                Nodes:
                [
                    new ProcedureNode("start", "intake", new Dictionary<string, string>()),
                    new ProcedureNode("check", "verify", new Dictionary<string, string>()),
                ],
                Edges:
                [
                    new ProcedureEdge("start", "accepted", "check"),
                ]));
    }

    private static AppProcedureDefinition DefinitionWithCycle()
    {
        return new AppProcedureDefinition(
            ProjectId: Guid.NewGuid(),
            ProcedureKey: "cyclic",
            GitRef: "refs/heads/main",
            Graph: new ProcedureGraph(
                Nodes:
                [
                    new ProcedureNode("a", "intake", new Dictionary<string, string>()),
                    new ProcedureNode("b", "verify", new Dictionary<string, string>()),
                ],
                Edges:
                [
                    new ProcedureEdge("a", "accepted", "b"),
                    new ProcedureEdge("b", "passed", "a"),
                ]));
    }

    private static AppProcedureDefinition DefinitionWithUnknownPort()
    {
        return new AppProcedureDefinition(
            ProjectId: Guid.NewGuid(),
            ProcedureKey: "bad-port",
            GitRef: "refs/heads/main",
            Graph: new ProcedureGraph(
                Nodes:
                [
                    new ProcedureNode("start", "intake", new Dictionary<string, string>()),
                    new ProcedureNode("check", "verify", new Dictionary<string, string>()),
                ],
                Edges:
                [
                    new ProcedureEdge("start", "nonexistent", "check"),
                ]));
    }

    private static AppProcedureDefinition DefinitionWithUnknownKind()
    {
        return new AppProcedureDefinition(
            ProjectId: Guid.NewGuid(),
            ProcedureKey: "bad-kind",
            GitRef: "refs/heads/main",
            Graph: new ProcedureGraph(
                Nodes:
                [
                    new ProcedureNode("start", "unknown-kind", new Dictionary<string, string>()),
                ],
                Edges: []));
    }
}
