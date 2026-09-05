using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Knowledge.Domain;
using Comuki.Modules.Knowledge.Infrastructure.Configuration;
using Comuki.Modules.Knowledge.Infrastructure.Embeddings;
using Comuki.Modules.Knowledge.Infrastructure.Hosted;
using Comuki.Modules.Knowledge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Knowledge.Infrastructure;

/// <summary>
/// Knowledge module composition — registers the embedding client
/// (OpenAI / Noop), the ingestor + searcher over the memory schema, and
/// the (optional) hosted service that polls the corpus. The pgvector
/// column lives in the memory schema and is reached through the shared
/// <see cref="IDbContextFactory{MemoryDbContext}"/> — the
/// <see cref="Memory.Infrastructure.Persistence.Stores.MemoryEmbeddingSql"/>
/// helpers carry the raw SQL. The cross-module project reference is
/// the pragmatic exception: the knowledge layer writes the same tables
/// the memory layer owns, and lifting those tables into a shared kernel
/// would be more invasive than the reference itself.
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
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<KnowledgeEmbeddingOptions>()
            .Bind(configuration.GetSection(KnowledgeEmbeddingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<KnowledgeIngestOptions>()
            .Bind(configuration.GetSection(KnowledgeIngestOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        // Embedding client selection — the same singleton instance is
        // resolved by every ingestion and search. Provider changes are
        // rare; the host restart cycle absorbs them. The OpenAI client
        // owns its HttpClient; the typed factory below threads the
        // base address through AddHttpClient so the host can override
        // it through configuration (Knowledge:Embedding:OpenAiBaseUrl)
        // without re-touching this registration.
        services.AddHttpClient<IEmbeddingClient, OpenAIEmbeddingClient>(static (httpClient, sp) =>
        {
            var options = sp.GetRequiredService<IOptions<KnowledgeEmbeddingOptions>>().Value;
            httpClient.BaseAddress = new Uri("https://api.openai.com/");
            return new OpenAIEmbeddingClient(
                httpClient,
                options.ResolveApiKey()
                    ?? throw new InvalidOperationException(
                        "Knowledge:Embedding:Provider=openai requires Knowledge:Embedding:ApiKeyEnvRef to name an env var with the key."),
                options.Model,
                options.Dimensions,
                sp.GetRequiredService<ILogger<OpenAIEmbeddingClient>>());
        });

        services.AddSingleton<IEmbeddingClient>(static sp =>
        {
            var options = sp.GetRequiredService<IOptions<KnowledgeEmbeddingOptions>>().Value;
            return options.Kind switch
            {
                EmbeddingProviderKind.Noop => new NoopEmbeddingClient(options.Dimensions),
                EmbeddingProviderKind.OpenAi => sp.GetRequiredService<OpenAIEmbeddingClient>(),
                EmbeddingProviderKind.Voyage => throw new NotSupportedException(
                    "Knowledge:Embedding:Provider=voyage is reserved — no embedder is shipped yet; switch to openai or noop."),
                _ => throw new ArgumentOutOfRangeException(nameof(options.Kind), options.Kind, null),
            };
        });

        services.AddSingleton<PgKnowledgeIngestor>();
        services.AddSingleton<PgKnowledgeSearcher>();
        services.AddSingleton<IKnowledgeIngestor>(static sp => sp.GetRequiredService<PgKnowledgeIngestor>());
        services.AddSingleton<IKnowledgeSearcher>(static sp => sp.GetRequiredService<PgKnowledgeSearcher>());

        services.AddHostedService<KnowledgeIngestBackgroundService>();

        return services;
    }
}
