using System.Text;
using System.Text.Json;
using Comuki.TestFakeModel.Cassettes.Sse;
using Comuki.TestFakeModel.Cassettes.Wire;

namespace Comuki.TestFakeModel.Cassettes.Recording;

/// <summary>
/// Forwards one inbound request to the configured upstream, mirrors the
/// upstream's response back to the caller, and returns the (not yet
/// redacted — see <c>Redaction.CassetteRedactor</c>) captured
/// <see cref="CassetteResponse"/> for <see cref="CassetteRecordingState"/> to
/// classify and persist. Reads the whole upstream body before forwarding
/// it on, rather than piping bytes through live: this tool records
/// against a scoped, budget-capped eval run (design.md D5), not a
/// latency-sensitive production path, and buffering first is what lets a
/// single <c>SseFrameParser.Parse</c> call handle both wire shapes
/// uniformly instead of a stateful incremental SSE reader.
/// </summary>
/// <remarks>Creates the forwarder targeting <paramref name="upstreamBaseUrl"/>, using clients from <paramref name="httpClientFactory"/>.</remarks>
public sealed class CassetteUpstreamForwarder(Uri upstreamBaseUrl, IHttpClientFactory httpClientFactory)
{
    /// <summary>The named <see cref="IHttpClientFactory"/> client this forwarder resolves — registered once in <c>Hosting.CassetteModelServer</c>'s composition (plumbing, not a typed adapter — see http-resilience-refit.md §1).</summary>
    public const string HttpClientName = "cassette-upstream";

    /// <summary>Forwards <paramref name="context"/>'s request (with <paramref name="rawBody"/> already read) and writes the upstream's response onto it.</summary>
    public async Task<CassetteResponse> ForwardAsync(HttpContext context, string rawBody, CancellationToken cancellationToken)
    {
        using var upstreamRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(upstreamBaseUrl, context.Request.Path.Value ?? string.Empty))
        {
            Content = new StringContent(rawBody, Encoding.UTF8, "application/json"),
        };
        foreach (var header in context.Request.Headers)
        {
            if (header.Key is "Host" or "Content-Length" or "Content-Type")
            {
                continue;
            }

            upstreamRequest.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string>)header.Value);
        }

        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var upstreamResponse = await client.SendAsync(upstreamRequest, cancellationToken);
        var upstreamBody = await upstreamResponse.Content.ReadAsStringAsync(cancellationToken);
        var streamed = upstreamResponse.Content.Headers.ContentType?.MediaType?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) ?? false;

        context.Response.StatusCode = (int)upstreamResponse.StatusCode;
        context.Response.ContentType = streamed ? "text/event-stream" : "application/json";
        await context.Response.WriteAsync(upstreamBody, cancellationToken);

        if (streamed)
        {
            return new CassetteResponse((int)upstreamResponse.StatusCode, Streamed: true, Body: null, SseFrameParser.Parse(upstreamBody));
        }

        using var document = JsonDocument.Parse(upstreamBody);
        return new CassetteResponse((int)upstreamResponse.StatusCode, Streamed: false, document.RootElement.Clone(), Events: null);
    }
}
