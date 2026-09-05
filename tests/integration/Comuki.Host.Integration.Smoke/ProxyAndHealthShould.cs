using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Smoke;

/// <summary>
/// Proxy models + anonymous health coverage (S9 #9.6):
/// <list type="bullet">
///   <item><c>GET /v1/models</c> without a virtual-key bearer returns 401
///     from the host's VirtualKey authentication scheme.</item>
///   <item><c>GET /health</c> is the anonymous liveness probe — always
///     200 with <c>{ \"status\": \"ok\" }</c>.</item>
/// </list>
/// </summary>
public sealed class ProxyAndHealthShould(SmokeHostServer server) : IClassFixture<SmokeHostServer>
{
    private readonly SmokeHostServer server = server;

    [Fact(DisplayName = "Given no virtual key, when GET /v1/models, then 401 from the VirtualKey scheme")]
    public async Task ModelsWithoutVirtualKeyReturns401Async()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = server.CreateAnonymousClient();

        var response = await client.GetAsync("/v1/models", cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Given the anonymous health endpoint, when GET /health, then 200 with status=ok")]
    public async Task HealthEndpointReturnsOkAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = server.CreateAnonymousClient();

        var response = await client.GetAsync("/health", cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        payload.GetProperty("status").GetString().ShouldBe("ok");
    }
}
