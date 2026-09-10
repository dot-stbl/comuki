using System.Net;
using System.Text;
using Comuki.Modules.Intake.Infrastructure.Providers;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Intake.Unit;

/// <summary>
/// Test plumbing for provider tests: a recording HTTP handler (no real
/// network — the Refit proxies run over it) and a fake secret resolver
/// keyed by reference string. Request bodies are snapshotted on arrival —
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

/// <summary>
/// Test double for the shared-kernel <see cref="ISecretResolver"/>.
/// Keyed by reference string — bare names, <c>env:NAME</c>, and
/// <c>file:/path</c> all match against the same map so tests do not
/// have to care which scheme the production code parsed. Returns
/// <c>null</c> for unknown refs so the test author can stage a missing
/// secret without a stub.
/// </summary>
internal sealed class FakeSecretResolver : ISecretResolver
{
    public Dictionary<string, string> Map { get; } = [];

    public Task<string?> ResolveAsync(string? reference, CancellationToken cancellationToken = default)
    {
        return reference is { Length: > 0 } && Map.TryGetValue(reference, out var value)
            ? Task.FromResult<string?>(value)
            : Task.FromResult<string?>(null);
    }
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
        services.AddHttpClient(TrackerHttp.GitHubClient).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddHttpClient(TrackerHttp.GitLabClient).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddHttpClient(TrackerHttp.YandexTrackerClient).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddHttpClient(TrackerHttp.JiraClient).ConfigurePrimaryHttpMessageHandler(() => handler);
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
