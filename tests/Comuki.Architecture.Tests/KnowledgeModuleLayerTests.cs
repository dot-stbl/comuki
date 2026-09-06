using NetArchTest.Rules;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Layer rules for the Knowledge module (S10 #9): Domain is innermost,
/// Application sits on it through ports, no reach into engine/hosts.
/// The Knowledge module owns its own tables in the <c>knowledge</c>
/// schema and its own DbContext factory; it does not reach into the
/// memory module (cross-module edges go through Shared.Contracts).
/// </summary>
public sealed class KnowledgeModuleLayerTests
{
    private const string KnowledgeApplication = "Comuki.Modules.Knowledge.Application";
    private const string KnowledgeInfrastructure = "Comuki.Modules.Knowledge.Infrastructure";
    private const string MemoryApplication = "Comuki.Modules.Memory.Application";
    private const string MemoryInfrastructure = "Comuki.Modules.Memory.Infrastructure";
    private const string MemoryDomain = "Comuki.Modules.Memory.Domain";
    private const string Engine = "Comuki.Engine.Orchestration";
    private const string EngineCompute = "Comuki.Engine.Compute";
    private const string Host = "Comuki.Host";
    private const string Translator = "Comuki.Host.Translator";
    private const string Migrator = "Comuki.Migrator";

    [Fact]
    public void KnowledgeDomainMustNotDependOnOuterLayers()
    {
        var result = Types
            .InAssembly(typeof(Modules.Knowledge.Domain.SourceDocument).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                KnowledgeApplication,
                KnowledgeInfrastructure,
                MemoryApplication,
                MemoryInfrastructure,
                MemoryDomain,
                Engine,
                EngineCompute,
                Host,
                Translator,
                Migrator,
                "Comuki.Shared.Contracts")
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void KnowledgeApplicationMustNotDependOnInfrastructureOrEngineOrHosts()
    {
        // Application must not reach into Knowledge.Infrastructure or any
        // sibling module — ports live here, EF wiring lives below.
        var result = Types
            .InAssembly(typeof(Modules.Knowledge.Application.KnowledgeSearchHit).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                KnowledgeInfrastructure,
                MemoryApplication,
                MemoryInfrastructure,
                MemoryDomain,
                Engine,
                EngineCompute,
                Host,
                Translator,
                Migrator)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void KnowledgeModuleMustNotDependOnEngineOrMemoryModule()
    {
        // Modules ↛ engine (comuki-project-structure §2): the engine
        // reaches modules through contracts, never the reverse. The
        // Knowledge module owns its tables — no cross-module edge into
        // the Memory module (S10 #9 final carve-out).
        var domain = Types
            .InAssembly(typeof(Modules.Knowledge.Domain.SourceDocument).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Engine, EngineCompute, MemoryApplication, MemoryInfrastructure, MemoryDomain)
            .GetResult();
        var application = Types
            .InAssembly(typeof(Modules.Knowledge.Application.KnowledgeSearchHit).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Engine, EngineCompute, MemoryApplication, MemoryInfrastructure, MemoryDomain)
            .GetResult();
        var infrastructure = Types
            .InAssembly(typeof(Modules.Knowledge.Infrastructure.Persistence.KnowledgeDbContext).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Engine, EngineCompute, MemoryApplication, MemoryInfrastructure, MemoryDomain)
            .GetResult();

        Assert.True(domain.IsSuccessful, Failing(domain));
        Assert.True(application.IsSuccessful, Failing(application));
        Assert.True(infrastructure.IsSuccessful, Failing(infrastructure));
    }

    private static string Failing(NetArchTest.Rules.TestResult result)
    {
        return $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}";
    }
}
