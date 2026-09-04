using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Proxy;

/// <summary>
/// End-to-end proxy behaviour (issue #8 / S9 T9.6):
/// <list type="bullet">
///   <item>missing / invalid bearer → 401 from the host's VirtualKey scheme</item>
///   <item>valid bearer → 200 with the fake upstream's body; Authorization
///     header rewritten to the upstream API key before forwarding</item>
/// </list>
/// Body-based usage metering (the <c>IProxyUsageExtractor</c> family) is
/// deferred — YARP's response-transform contract forbids body reads, so
/// v1 ships auth + passthrough only; the unit suite covers the meter
/// path end-to-end.
/// </summary>
public sealed class ProxyPassthroughShould : IAsyncLifetime
{
    private const string VirtualKey = "vkey_test_alpha";
    private const string RequestBody = /*lang=json,strict*/ """{"model":"gpt-4o-mini","messages":[{"role":"user","content":"hi"}]}""";

    private HostProxyServer server = null!;

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        server = new HostProxyServer();
        return server.InitializeAsync();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return server.DisposeAsync();
    }

    [Fact(DisplayName = "Given a chat completion without an Authorization header, when POST /v1/chat/completions, then 401")]
    public async Task AnonymousRequestReturns401Async()
    {
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(RequestBody, Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Given an unknown virtual key, when POST /v1/chat/completions, then 401")]
    public async Task UnknownKeyReturns401Async()
    {
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(RequestBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "vkey_unknown");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Given a valid virtual key, when POST /v1/chat/completions, then the fake upstream receives the request with the upstream API key in Authorization")]
    public async Task ValidKeyForwardsAndRewritesAuthAsync()
    {
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(RequestBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", VirtualKey);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var errorBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            throw new Xunit.Sdk.XunitException($"Expected 200 but got {(int)response.StatusCode}: {errorBody}");
        }

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("gpt-4o-mini");

        var seen = server.FakeUpstream.Seen;

        seen.ShouldHaveSingleItem();
        seen[0].Path.ShouldBe("/v1/chat/completions");
        seen[0].Authorization.ShouldNotBeNull();
        seen[0].Authorization.ShouldBe("Bearer sk-fake");
    }
}
