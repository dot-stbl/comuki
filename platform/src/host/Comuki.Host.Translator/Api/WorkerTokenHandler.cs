using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Translator.Api;

/// <summary>
/// DelegatingHandler that stamps the worker token on every orchestrator
/// call as a Bearer credential. The token is fixed for the container's
/// lifetime.
/// </summary>
/// <param name="options"></param>
public sealed class WorkerTokenHandler(IOptions<TranslatorOptions> options) : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.WorkerToken);
        return base.SendAsync(request, cancellationToken);
    }
}
