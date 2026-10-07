using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Knowledge.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Knowledge.Infrastructure.Embeddings;

/// <summary>
/// OpenAI-backed <see cref="IEmbeddingClient"/>: vectorises text through
/// the <c>/v1/embeddings</c> REST endpoint via a Refit-generated
/// <see cref="IOpenAIEmbeddingsApi"/> proxy. The <c>apiKey</c> is read
/// from the env var named by
/// <see cref="KnowledgeEmbeddingOptions.ApiKeyEnvRef"/>;
/// the constructor throws when the env var is unset so misconfiguration
/// is caught at boot, not on the first embed call. The dimension guard
/// surfaces a single, stable error when the provider's response shape
/// disagrees with the configured
/// <see cref="KnowledgeEmbeddingOptions.Dimensions"/> —
/// the same contract the pre-Refit hand-rolled HTTP client enforced.
/// </summary>
public sealed class OpenAIEmbeddingClient : IEmbeddingClient
{
    private readonly IOpenAIEmbeddingsApi api;
    private readonly KnowledgeEmbeddingOptions options;
    private readonly ILogger<OpenAIEmbeddingClient> logger;

    /// <summary>Constructs the OpenAI-backed embedder. The API key is NOT
    /// validated here — that's deferred to <see cref="SendAsync"/> so
    /// the singleton can be resolved under a Noop configuration
    /// (where the ctor's key check would throw and the singleton
    /// would be unavailable, breaking the IEmbeddingClient switch).</summary>
    /// <param name="api">Refit-generated OpenAI client (DI-registered in <c>KnowledgeInfrastructureExtensions</c>).</param>
    /// <param name="options">Embedding options — model, dimensions, API key env-var name.</param>
    /// <param name="logger">Structured logger.</param>
    public OpenAIEmbeddingClient(
        IOpenAIEmbeddingsApi api,
        IOptions<KnowledgeEmbeddingOptions> options,
        ILogger<OpenAIEmbeddingClient> logger)
    {
        this.api = api;
        this.options = options.Value;
        this.logger = logger;
        this.logger.LogInformation(
            "openai embedding client ready (model {Model}, dimensions {Dimensions})",
            this.options.Model,
            this.options.Dimensions);
    }

    /// <inheritdoc />
    public string ProviderName => "openai";

    /// <inheritdoc />
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        // Single-text: forward the batch result (or surface its error).
        // The previous shape wrapped SendAsync in
        // TaskContinuationOptions.OnlyOnRanToCompletion which
        // collapsed the validation/dimension failures into a
        // TaskCanceledException — the caller couldn't tell a real
        // error from a cancellation. Awaiting the batch directly
        // preserves the exception type.
        var vectors = await SendAsync([text], cancellationToken).ConfigureAwait(false);
        return vectors[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        return await SendAsync(texts, cancellationToken);
    }

    private async Task<IReadOnlyList<float[]>> SendAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        // Lazy API-key validation: the IEmbeddingClient switch can
        // resolve this singleton under a Noop config (so it never
        // touches the OpenAI path), but the moment an OpenAI embed is
        // attempted the env var has to be set. Ctor-time validation
        // would make the singleton unresolvable under Noop and break
        // the switch.
        var apiKey = options.ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OpenAI API key is required for knowledge embeddings; set Knowledge:Embedding:ApiKeyEnvRef to the env var name carrying the key.");
        }

        var response = await api.CreateAsync(
            new EmbeddingRequest(Model: options.Model, Input: texts),
            cancellationToken).ConfigureAwait(false);

        var vectors = new float[response.Data.Count][];
        for (var index = 0; index < response.Data.Count; index++)
        {
            var embedding = response.Data[index].Embedding;
            if (embedding.Length != options.Dimensions)
            {
                throw new InvalidOperationException(
                    $"embedding model returned {embedding.Length} dimensions but Knowledge:Embedding:Dimensions is configured as {options.Dimensions} — adjust the config or pick a model that matches.");
            }

            vectors[index] = embedding;
        }

        return vectors;
    }
}
