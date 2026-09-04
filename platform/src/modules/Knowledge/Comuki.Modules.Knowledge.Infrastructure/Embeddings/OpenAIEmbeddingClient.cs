using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Comuki.Modules.Knowledge.Application;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Knowledge.Infrastructure.Embeddings;

/// <summary>
/// Embedding client that talks to the OpenAI <c>/v1/embeddings</c> REST
/// endpoint directly through <see cref="HttpClient"/>. The
/// <c>apiKey</c> is read from the env var named by
/// <see cref="Configuration.KnowledgeEmbeddingOptions.ApiKeyEnvRef"/>;
/// the constructor throws when the env var is unset so misconfiguration
/// is caught at boot, not on the first embed call. The raw HTTP path
/// keeps the dependency surface tight (no OpenAI SDK) and is easy to
/// mock in tests — wire-format is the canonical contract.
/// </summary>
public sealed partial class OpenAIEmbeddingClient : IEmbeddingClient
{
    private readonly HttpClient httpClient;
    private readonly string model;
    private readonly int dimensions;

    /// <summary>Constructs the OpenAI-backed embedder; throws when the API key is missing.</summary>
    /// <param name="httpClient">Injected by the DI container — base address set to <c>https://api.openai.com/</c>.</param>
    /// <param name="apiKey"></param>
    /// <param name="model"></param>
    /// <param name="dimensions"></param>
    /// <param name="logger"></param>
    public OpenAIEmbeddingClient(HttpClient httpClient, string apiKey, string model, int dimensions, ILogger<OpenAIEmbeddingClient> logger)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OpenAI API key is required for knowledge embeddings; set Knowledge:Embedding:ApiKeyEnvRef to the env var name carrying the key.");
        }

        this.httpClient = httpClient;
        this.httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        this.model = model;
        this.dimensions = dimensions;
        logger.LogInformation("openai embedding client ready (model {Model}, dimensions {Dimensions})", model, dimensions);
    }

    /// <inheritdoc />
    public string ProviderName => "openai";

    /// <inheritdoc />
    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        return SendAsync([text], cancellationToken).ContinueWith(
            static task => task.Result[0],
            cancellationToken,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        return await SendAsync(texts, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<float[]>> SendAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var request = new EmbeddingRequest
        {
            Model = model,
            Input = texts,
        };

        using var response = await httpClient.PostAsJsonAsync("v1/embeddings", request, OpenAiJsonContext.Default.EmbeddingRequest, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync(OpenAiJsonContext.Default.EmbeddingResponse, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("openai returned an empty response body");

        var vectors = new float[payload.Data.Count][];
        for (var index = 0; index < payload.Data.Count; index++)
        {
            var embedding = payload.Data[index].Embedding;
            if (embedding.Length != dimensions)
            {
                throw new InvalidOperationException(
                    $"embedding model returned {embedding.Length} dimensions but Knowledge:Embedding:Dimensions is configured as {dimensions} — adjust the config or pick a model that matches.");
            }

            vectors[index] = embedding;
        }

        return vectors;
    }

    private sealed class EmbeddingRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("input")]
        public required IReadOnlyList<string> Input { get; init; }
    }

    private sealed class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public required IReadOnlyList<EmbeddingDatum> Data { get; init; }
    }

    private sealed class EmbeddingDatum
    {
        [JsonPropertyName("embedding")]
        public required float[] Embedding { get; init; }
    }

    [JsonSerializable(typeof(EmbeddingRequest))]
    [JsonSerializable(typeof(EmbeddingResponse))]
    private sealed partial class OpenAiJsonContext : JsonSerializerContext
    {
    }
}
