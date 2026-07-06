using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace Comuki.Platform.Proxy.Integration.Rotation;

public sealed class KeyRotationEndToEndShould
{
    [Fact(DisplayName = "Given the first Z.AI key is exhausted, when a worker posts a message, then the proxy rotates to a live key and the client sees success")]
    public async Task RotateToLiveKeyWhenFirstIsDead()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var upstream = await FakeUpstream.StartAsync();

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(webHost =>
            {
                webHost.UseSetting("Routing:Rotation:UpstreamUrl", upstream.Url);
                webHost.UseSetting("Routing:Rotation:DefaultCooldown", "01:00:00");
                webHost.UseSetting("Routing:Rotation:ApiKeys:0", "dead");
                webHost.UseSetting("Routing:Rotation:ApiKeys:1", "live");
                webHost.UseSetting("Routing:Rotation:ExhaustionRules:0:StatusCode", "429");
            });

        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/v1/messages",
            new StringContent("""{"model":"glm","messages":[]}"""),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(ct);
        body.ShouldContain("ok-from-live-key");
    }

    [Fact(DisplayName = "Given every Z.AI key is exhausted, when a worker posts a message, then the proxy responds 503")]
    public async Task Return503WhenAllKeysExhausted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var upstream = await FakeUpstream.StartAsync();

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(webHost =>
            {
                webHost.UseSetting("Routing:Rotation:UpstreamUrl", upstream.Url);
                webHost.UseSetting("Routing:Rotation:DefaultCooldown", "01:00:00");
                webHost.UseSetting("Routing:Rotation:ApiKeys:0", "dead");
                webHost.UseSetting("Routing:Rotation:ExhaustionRules:0:StatusCode", "429");
            });

        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/v1/messages",
            new StringContent("""{"model":"glm","messages":[]}"""),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }
}
