using NetArchTest.Rules;
using Xunit;

namespace Comuki.Modules.Knowledge.DomainLayerTests;

/// <summary>
/// Cross-module isolation assertions for the Knowledge module — a
/// follow-up to the S10 #9 carve-out: when the <c>source_documents</c>
/// and <c>memory_embeddings</c> entities moved out of Memory.Domain and
/// into Knowledge.Domain, the Knowledge module must not regress to
/// reaching back into the Memory module. The rule mirrors the
/// KnowledgeModuleLayerTests in Comuki.Architecture.Tests and is
/// duplicated here so a single reviewer can run one focused suite
/// per module without dragging the entire architecture test assembly.
/// </summary>
public sealed class KnowledgeDomainLayerTests
{
    private const string MemoryApplication = "Comuki.Modules.Memory.Application";
    private const string MemoryInfrastructure = "Comuki.Modules.Memory.Infrastructure";
    private const string MemoryDomain = "Comuki.Modules.Memory.Domain";

    [Fact(DisplayName = "When knowledge Domain Must Not Depend On Memory Module, then test passes")]
    public void KnowledgeDomainMustNotDependOnMemoryModule()
    {
        // The Knowledge.Domain assembly must be free of any Memory module
        // dependency. The carve-out from S10 #9 was the whole point of
        // moving SourceDocument/MemoryEmbedding across — they live here
        // now.
        var result = Types
            .InAssembly(typeof(Domain.SourceDocument).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                MemoryDomain,
                MemoryApplication,
                MemoryInfrastructure)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact(DisplayName = "When knowledge Application Must Not Depend On Memory Module, then test passes")]
    public void KnowledgeApplicationMustNotDependOnMemoryModule()
    {
        // Knowledge.Application used to depend on Memory.Domain.Knowledge
        // for the entity types. The carve-out removed that edge: the
        // application now references Knowledge.Domain only.
        var result = Types
            .InAssembly(typeof(Application.KnowledgeSearchHit).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                MemoryDomain,
                MemoryApplication,
                MemoryInfrastructure)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact(DisplayName = "When knowledge Infrastructure Must Not Depend On Memory Module, then test passes")]
    public void KnowledgeInfrastructureMustNotDependOnMemoryModule()
    {
        // Knowledge.Infrastructure owns its own DbContext factory +
        // SQL helpers. It must not reach into Memory.Infrastructure for
        // the context or the embedding SQL — that's the whole
        // architecture fix.
        var result = Types
            .InAssembly(typeof(Infrastructure.Persistence.KnowledgeDbContext).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                MemoryDomain,
                MemoryApplication,
                MemoryInfrastructure)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
