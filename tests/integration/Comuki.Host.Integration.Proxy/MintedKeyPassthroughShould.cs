using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Comuki.Modules.Proxy.Application.Options;
using Comuki.Modules.Proxy.Application.Ports;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Proxy;

/// <summary>
/// The proxy accepts a runtime-minted virtual key exactly like a
/// config-seeded one (issue #122): the mint resolves through the same
/// auth path, attributes the project, forwards to the inherited upstream
/// and swaps the minted bearer for the upstream API key on the outbound
/// hop — the mint is a capability, never an upstream secret. An expired
/// mint is rejected.
/// </summary>
[Collection(nameof(ProxyIntegrationCollection))]
public sealed class MintedKeyPassthroughShould : IAsyncLifetime
{
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

    [Fact(DisplayName = "Given a minted key for a configured project, when POST /v1/chat/completions, then it passes auth and the upstream sees the provider key, not the mint")]
    public async Task AcceptMintedKeyAndSwapForTheUpstreamKeyAsync()
    {
        var store = server.Application.Services.GetRequiredService<IVirtualKeyStore>();
        var projectId = server.Application.Services.GetRequiredService<IOptions<ProxyOptions>>()
            .Value.VirtualKeys[0].ProjectId;
        var workItemId = Guid.NewGuid();
        await store.MintAsync(
            "minted_passthrough_live",
            new ProjectId(projectId),
            workItemId,
            DateTimeOffset.UtcNow.AddMinutes(2),
            cancellationToken: TestContext.Current.CancellationToken);

        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(RequestBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "minted_passthrough_live");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var seen = server.FakeUpstream.Seen;
        seen.ShouldHaveSingleItem();
        seen[0].Authorization.ShouldBe("Bearer sk-fake", "the minted token must be swapped for the upstream key before forwarding");
    }

    [Fact(DisplayName = "Given a minted key past its lease deadline, when POST /v1/chat/completions, then 401 — expiry alone kills the mint")]
    public async Task RejectExpiredMintedKeyAsync()
    {
        var store = server.Application.Services.GetRequiredService<IVirtualKeyStore>();
        var projectId = server.Application.Services.GetRequiredService<IOptions<ProxyOptions>>()
            .Value.VirtualKeys[0].ProjectId;
        await store.MintAsync(
            "minted_passthrough_expired",
            new ProjectId(projectId),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            cancellationToken: TestContext.Current.CancellationToken);

        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(RequestBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "minted_passthrough_expired");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Given a revoked minted key, when POST /v1/chat/completions, then 401 immediately — no deletion grace for mints")]
    public async Task RejectRevokedMintedKeyAtOnceAsync()
    {
        var store = server.Application.Services.GetRequiredService<IVirtualKeyStore>();
        var projectId = server.Application.Services.GetRequiredService<IOptions<ProxyOptions>>()
            .Value.VirtualKeys[0].ProjectId;
        await store.MintAsync(
            "minted_passthrough_revoked",
            new ProjectId(projectId),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(2),
            cancellationToken: TestContext.Current.CancellationToken);
        await store.RemoveAsync("minted_passthrough_revoked", TestContext.Current.CancellationToken);

        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(RequestBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "minted_passthrough_revoked");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

/// <summary>
/// Serialises the proxy suites on one container at a time — two full-host
/// Testcontainers booting concurrently starve each other (the same contract
/// <c>WorkersIntegrationCollection</c> documents).
/// </summary>
[CollectionDefinition(nameof(ProxyIntegrationCollection), DisableParallelization = true)]
public sealed class ProxyIntegrationCollection;
