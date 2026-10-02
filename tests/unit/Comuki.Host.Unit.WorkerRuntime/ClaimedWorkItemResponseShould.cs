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

    [Fact(DisplayName = "Given source-git fields set, when serialized, then sourceGitUrl/sourceGitRef/gitCredential ride on the wire")]
    public void SerializeSourceGitFieldsWhenSet()
    {
        var response = NewResponse(
            SourceGitUrl: "https://github.com/example/private.git",
            SourceGitRef: "release/1.x",
            GitCredential: "raw-token-do-not-leak");

        var json = JsonSerializer.Serialize(response, JsonSerializerOptions.Web);

        json.ShouldContain("sourceGitUrl", Case.Insensitive);
        json.ShouldContain("sourceGitRef", Case.Insensitive);
        json.ShouldContain("gitCredential", Case.Insensitive);
        json.ShouldContain("https://github.com/example/private.git");
        json.ShouldContain("release/1.x");
    }

    [Fact(DisplayName = "Given source-git fields unset, when serialized, then sourceGitUrl/sourceGitRef/gitCredential are omitted")]
    public void OmitSourceGitFieldsWhenUnset()
    {
        var response = NewResponse();

        var json = JsonSerializer.Serialize(response, JsonSerializerOptions.Web);

        json.ShouldNotContain("sourceGitUrl", Case.Insensitive);
        json.ShouldNotContain("sourceGitRef", Case.Insensitive);
        json.ShouldNotContain("gitCredential", Case.Insensitive);
    }

    private static ClaimedWorkItemResponse NewResponse(
        string? ProxyBaseUrl = null,
        string? VirtualKey = null,
        string? SourceGitUrl = null,
        string? SourceGitRef = null,
        string? GitCredential = null)
    {
        return new ClaimedWorkItemResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "implement",
            "net10-sdk-bun",
            "do it",
            DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds(),
            1,
            1,
            ProxyBaseUrl,
            VirtualKey,
            SourceGitUrl,
            SourceGitRef,
            GitCredential);
    }
}
