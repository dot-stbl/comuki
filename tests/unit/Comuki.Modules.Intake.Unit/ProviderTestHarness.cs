using System.Net;
using System.Text;
using Comuki.Modules.Intake.Application.Ports.Sources;
using Comuki.Modules.Intake.Infrastructure.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Intake.Unit;

/// <summary>
/// Test plumbing for provider tests: a recording HTTP handler (no real
/// network — the Refit proxies run over it) and a fake secret resolver
/// keyed by env-var name. Request bodies are snapshotted on arrival —
/// Refit disposes the content stream after the call.
/// </summary>
internal sealed class RecordedRequest
{
    public required HttpRequestMessage Message { get; init; }

    public required string Body { get; init; }
}

internal sealed class RecordingHandler : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    public required Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(new RecordedRequest
        {
            Message = request,
            Body = await ReadBodyAsync(request.Content),
        });
        return Respond(request);
    }

    private static async Task<string> ReadBodyAsync(HttpContent? content)
    {
        if (content is null)
        {
            return string.Empty;
        }

        var stream = await content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}

internal sealed class FakeSecretResolver : ISecretResolver
{
    public string? Resolve(string? envName)
    {
        return envName is { Length: > 0 } && Map.TryGetValue(envName, out var value) ? value : null;
    }

    public Dictionary<string, string> Map { get; } = [];
}

internal static class ProviderTestHarness
{
    /// <summary>Shared empty header map (collection expressions cannot target IReadOnlyDictionary).</summary>
    public static readonly IReadOnlyDictionary<string, string> NoHeaders = new Dictionary<string, string>();

    /// <summary>Shared empty query map.</summary>
    public static readonly IReadOnlyDictionary<string, string> NoQuery = new Dictionary<string, string>();

    public static (TrackerClientFactory Factory, RecordingHandler Handler) CreateFactory()
    {
        var handler = new RecordingHandler
        {
            Respond = static request => new HttpResponseMessage(HttpStatusCode.OK),
        };

        var services = new ServiceCollection();
        _ = services.AddHttpClient(TrackerHttp.GitHubClient).ConfigurePrimaryHttpMessageHandler(() => handler);
        _ = services.AddHttpClient(TrackerHttp.GitLabClient).ConfigurePrimaryHttpMessageHandler(() => handler);
        _ = services.AddHttpClient(TrackerHttp.YandexTrackerClient).ConfigurePrimaryHttpMessageHandler(() => handler);
        _ = services.AddHttpClient(TrackerHttp.JiraClient).ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();

        return (new TrackerClientFactory(provider.GetRequiredService<IHttpClientFactory>()), handler);
    }

    public static HttpResponseMessage Json(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
