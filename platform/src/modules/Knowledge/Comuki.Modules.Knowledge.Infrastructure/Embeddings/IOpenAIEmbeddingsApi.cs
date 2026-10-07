using System.Text.Json.Serialization;
using Refit;

namespace Comuki.Modules.Knowledge.Infrastructure.Embeddings;

/// <summary>
/// Refit interface for the OpenAI <c>/v1/embeddings</c> REST endpoint. The
/// production client <see cref="OpenAIEmbeddingClient"/> consumes this
/// through the standard Refit pipeline (DI-registered in
/// <see cref="KnowledgeInfrastructureExtensions"/>); tests substitute
/// the interface directly with NSubstitute. Bearer auth and resilience
/// are configured at registration time — the interface stays free of
/// both.
/// </summary>
public interface IOpenAIEmbeddingsApi
{
    /// <summary>POST /v1/embeddings — vectorise <see cref="EmbeddingRequest.Input"/> under <see cref="EmbeddingRequest.Model"/>.</summary>
    /// <param name="request">The model + batch of input strings.</param>
    /// <param name="cancellationToken">Forwarded to the HTTP client.</param>
    [Post("/v1/embeddings")]
    public Task<EmbeddingResponse> CreateAsync(
        [Body] EmbeddingRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>OpenAI wire shape for the embeddings request body — model name + batch of input strings.</summary>
/// <param name="Model">Provider-specific model (e.g. <c>text-embedding-3-small</c>).</param>
/// <param name="Input">One or more texts to embed; the OpenAI endpoint accepts a list (it batches internally).</param>
public sealed record EmbeddingRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

/// <summary>OpenAI wire shape for the embeddings response body — a list of <see cref="EmbeddingDatum"/>.</summary>
/// <param name="Data">One datum per input, in input order.</param>
public sealed record EmbeddingResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingDatum> Data);

/// <summary>One embedding vector in the OpenAI response — the float array is what the production client validates against <c>Knowledge:Embedding:Dimensions</c>.</summary>
/// <param name="Embedding">Raw embedding vector from the model. Length must match the configured dimension.</param>
public sealed record EmbeddingDatum(
    [property: JsonPropertyName("embedding")] float[] Embedding);
