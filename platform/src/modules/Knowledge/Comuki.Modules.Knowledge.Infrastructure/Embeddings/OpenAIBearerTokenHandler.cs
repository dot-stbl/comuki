using System.Net.Http.Headers;
using Comuki.Modules.Knowledge.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Knowledge.Infrastructure.Embeddings;

/// <summary>
/// DelegatingHandler that stamps the OpenAI API key as a Bearer credential
/// on every <c>/v1/embeddings</c> request. The key is resolved from
/// <see cref="KnowledgeEmbeddingOptions.ApiKeyEnvRef"/> per call so a
/// rotation (env var change) takes effect without re-binding the client;
/// the credential itself is never logged.
/// </summary>
/// <param name="options">Embedding options — carries the env-var name that resolves to the API key.</param>
public sealed class OpenAIBearerTokenHandler(IOptions<KnowledgeEmbeddingOptions> options) : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // boundary: HTTP Authorization header — bearer credential lives
        // only in this header, never in logs. The ResolveApiKey call
        // is cheap (Environment.GetEnvironmentVariable lookup) and
        // safe to run per request.
        var apiKey = options.Value.ResolveApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey) && request.Headers.Authorization is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
