using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace Comuki.Platform.Proxy.Integration.Rotation;

public sealed class KeyRotationEndToEndTests
{
    [Fact]
    public async Task DeadKeyFirst_RotatesToLiveKey_ClientSeesSuccess()
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

    [Fact]
    public async Task AllKeysDead_ClientGets503()
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
