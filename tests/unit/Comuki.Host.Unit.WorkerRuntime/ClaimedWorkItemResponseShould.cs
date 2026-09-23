using System.Text.Json;
using Comuki.Host.Workers.Api;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.WorkerRuntime;

/// <summary>
/// Wire contract of the claim response (issue #122): the proxy fields
/// are optional — present only when a key was minted — and serialize
/// camelCase like the rest of the worker surface.
/// </summary>
public sealed class ClaimedWorkItemResponseShould
{
    [Fact(DisplayName = "Given a minted claim, when serialized, then proxyBaseUrl and virtualKey ride on the wire")]
    public void SerializeProxyFieldsWhenMinted()
    {
        var response = NewResponse(ProxyBaseUrl: "http://comuki-proxy:8080", VirtualKey: "minted_token_abc");

        var json = JsonSerializer.Serialize(response, JsonSerializerOptions.Web);

        json.ShouldContain("proxyBaseUrl", Case.Insensitive);
        json.ShouldContain("virtualKey", Case.Insensitive);
        json.ShouldContain("minted_token_abc");
        json.ShouldContain("http://comuki-proxy:8080");
    }

    [Fact(DisplayName = "Given a claim without a mint, when serialized, then both proxy fields are omitted")]
    public void OmitProxyFieldsWhenNotMinted()
    {
        var response = NewResponse();

        var json = JsonSerializer.Serialize(response, JsonSerializerOptions.Web);

        json.ShouldNotContain("proxyBaseUrl", Case.Insensitive);
        json.ShouldNotContain("virtualKey", Case.Insensitive);
    }

    private static ClaimedWorkItemResponse NewResponse(string? ProxyBaseUrl = null, string? VirtualKey = null)
    {
        return new ClaimedWorkItemResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "implement",
            "do it",
            DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds(),
            1,
            ProxyBaseUrl,
            VirtualKey);
    }
}
