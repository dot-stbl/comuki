using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Knowledge.Infrastructure;
using Comuki.Modules.Knowledge.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Knowledge.Unit;

/// <summary>
/// The embedding-client slice registration: a host that embeds but does
/// not own the knowledge schema (the brain host's memory tools) gets
/// exactly <see cref="IEmbeddingClient"/> — no ingestor, no searcher, no
/// background worker — and the full module install still layers it in
/// unchanged.
/// </summary>
public sealed class KnowledgeEmbeddingClientRegistrationShould
{
    [Fact(DisplayName = "Given the embedding slice only, when the provider resolves, then IEmbeddingClient answers and nothing else was dragged in")]
    public void RegisterTheEmbeddingClientWithoutTheModule()
    {
        var services = new ServiceCollection();

        services.AddKnowledgeEmbeddingClient(EmptyConfiguration());

        using var provider = services.BuildServiceProvider();
        var embedder = provider.GetRequiredService<IEmbeddingClient>();
        embedder.ProviderName.ShouldBe("noop");
        provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<KnowledgeEmbeddingOptions>>()
            .Value.Dimensions.ShouldBe(1536);
        provider.GetService<IKnowledgeIngestor>().ShouldBeNull();
        provider.GetService<IKnowledgeSearcher>().ShouldBeNull();
    }

    [Fact(DisplayName = "Given the full module install, when the provider resolves, then the embedding client still answers once")]
    public void LayerTheSliceUnderTheFullModule()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // the ingestor's context factory + scope accessor are persistence's
        // and the host's to provide; the layering question here is only
        // about the embedding registration
        services.AddSingleton(NSubstitute.Substitute.For<Microsoft.EntityFrameworkCore.IDbContextFactory<Infrastructure.Persistence.KnowledgeDbContext>>());
        services.AddSingleton(NSubstitute.Substitute.For<Shared.Kernel.Scoping.ISubjectScopeAccessor>());

        services.AddKnowledgeInfrastructure(EmptyConfiguration());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IEmbeddingClient>().ProviderName.ShouldBe("noop");
        provider.GetRequiredService<IKnowledgeIngestor>().ShouldNotBeNull();
    }

    private static IConfiguration EmptyConfiguration()
    {
        return new ConfigurationBuilder().AddInMemoryCollection().Build();
    }
}
