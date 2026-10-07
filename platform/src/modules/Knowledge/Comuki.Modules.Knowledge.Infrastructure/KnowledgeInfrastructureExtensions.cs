using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Knowledge.Application.Documents;
using Comuki.Modules.Knowledge.Domain;
using Comuki.Modules.Knowledge.Infrastructure.Configuration;
using Comuki.Modules.Knowledge.Infrastructure.Embeddings;
using Comuki.Modules.Knowledge.Infrastructure.Hosted;
using Comuki.Modules.Knowledge.Infrastructure.Persistence;
using Comuki.Shared.Bootstrap.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Refit;

namespace Comuki.Modules.Knowledge.Infrastructure;

/// <summary>
/// Knowledge module composition — registers the embedding client
/// (OpenAI / Noop), the ingestor + searcher over the <c>knowledge</c>
/// schema, and the (optional) hosted service that polls the corpus.
/// The pgvector column lives in the knowledge schema and is reached
/// through the shared <see cref="IDbContextFactory{KnowledgeDbContext}"/>
/// — the <see cref="Persistence.Stores.EmbeddingSql"/> helpers carry
/// the raw SQL.
/// </summary>
public static class KnowledgeInfrastructureExtensions
{
    /// <summary>Registers the Knowledge infrastructure services.</summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    public static IServiceCollection AddKnowledgeInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddKnowledgeEmbeddingClient(configuration);

        services
            .AddOptions<KnowledgeIngestOptions>()
            .Bind(configuration.GetSection(KnowledgeIngestOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<PgKnowledgeIngestor>();
        services.AddSingleton<PgKnowledgeSearcher>();
        services.AddSingleton<PgKnowledgeDocumentReader>();
        services.AddSingleton<IKnowledgeIngestor>(static sp => sp.GetRequiredService<PgKnowledgeIngestor>());
        services.AddSingleton<IKnowledgeSearcher>(static sp => sp.GetRequiredService<PgKnowledgeSearcher>());
        services.AddSingleton<IKnowledgeDocumentReader>(static sp => sp.GetRequiredService<PgKnowledgeDocumentReader>());

        // IComukiWorker — registered here (not AddHostedService) so the
        // comuki worker registry owns the supervision loop. The same
        // shape as Comuki.Host.Artifacts.RunArtifactPackagerComukiWorker.
        services.AddSingleton<IComukiWorker, KnowledgeIngestComukiWorker>();

        return services;
    }

    /// <summary>
    /// Registers just the embedding client (options + OpenAI/Noop
    /// selection) — the slice other modules embed with. Extracted from
    /// <see cref="AddKnowledgeInfrastructure"/> so a host that only needs
    /// <see cref="IEmbeddingClient"/> (the brain host's memory tools)
    /// doesn't drag the knowledge schema, ingestor or the ingest worker
    /// along. The Knowledge:Embedding section is shared configuration:
    /// one provider, one dimension, every consumer.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    public static IServiceCollection AddKnowledgeEmbeddingClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<KnowledgeEmbeddingOptions>()
            .Bind(configuration.GetSection(KnowledgeEmbeddingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Refit-typed OpenAI client. The base address is pinned to
        // api.openai.com because the contract is hard-wired to OpenAI's
        // /v1/embeddings shape; Knowledge:Embedding:OpenAiBaseUrl (if
        // reintroduced) would need to override this. The bearer handler
        // resolves the API key from the env var on every call, so a
        // key rotation takes effect without re-binding the client. The
        // standard resilience handler (retry + circuit breaker + total
        // timeout) is wired by the same call — every outbound embedding
        // request gets the same retry/timeout policy as the rest of
        // the host's outbound HTTP.
        services.AddTransient<OpenAIBearerTokenHandler>();
        services
            .AddRefitClient<IOpenAIEmbeddingsApi>()
            .ConfigureHttpClient(static client => client.BaseAddress = new Uri("https://api.openai.com/"))
            .AddHttpMessageHandler<OpenAIBearerTokenHandler>()
            .AddStandardResilienceHandler();

        // Concrete clients are singletons (stateless). The
        // IEmbeddingClient binding picks the right one by options.Kind
        // so a Noop config never instantiates the OpenAI client (which
        // would otherwise validate the env-var key at construction
        // and fail). The previous shape had two AddSingleton registrations
        // for IEmbeddingClient (the AddHttpClient typed-client factory
        // for OpenAI + a separate switch factory) — the latter is
        // now the only one, and the AddHttpClient is gone (Refit owns
        // the OpenAI HttpClient). NoopEmbeddingClient's ctor takes
        // a dimension count, so the factory pulls it from the bound
        // options rather than relying on DI to find a bare int.
        services.AddSingleton<OpenAIEmbeddingClient>();
        services.AddSingleton(static sp =>
            new NoopEmbeddingClient(
                sp.GetRequiredService<IOptions<KnowledgeEmbeddingOptions>>().Value.Dimensions));
        services.AddSingleton<IEmbeddingClient>(static sp =>
        {
            var options = sp.GetRequiredService<IOptions<KnowledgeEmbeddingOptions>>().Value;
            return options.Kind switch
            {
                EmbeddingProviderKind.Noop => sp.GetRequiredService<NoopEmbeddingClient>(),
                EmbeddingProviderKind.OpenAi => sp.GetRequiredService<OpenAIEmbeddingClient>(),
                EmbeddingProviderKind.Voyage => throw new NotSupportedException(
                    "Knowledge:Embedding:Provider=voyage is reserved — no embedder is shipped yet; switch to openai or noop."),
                _ => throw new ArgumentOutOfRangeException(nameof(options.Kind), options.Kind, null),
            };
        });

        return services;
    }
}
