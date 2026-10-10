using Comuki.Host.Translator.Api.Models.Responses;
using Comuki.Host.Translator.Execution;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// <see cref="PiEnvironment.FromClaim"/>: both proxy fields when the claim
/// carries a mint, nothing when it does not.
/// </summary>
public sealed class PiEnvironmentShould
{
    [Fact(DisplayName = "Given a claim with proxy fields, when mapped, then exactly ANTHROPIC_BASE_URL and ANTHROPIC_AUTH_TOKEN are stamped")]
    public void MapBothProxyFieldsFromAClaimWithAMint()
    {
        var claimed = NewClaim(ProxyBaseUrl: "http://comuki-proxy:8080", VirtualKey: "minted_token_abc");

        var environment = PiEnvironment.FromClaim(claimed);

        environment.ShouldNotBeNull();
        environment.Count.ShouldBe(2);
        environment[PiEnvironment.AnthropicBaseUrlVariable].ShouldBe("http://comuki-proxy:8080");
        environment[PiEnvironment.AnthropicAuthTokenVariable].ShouldBe("minted_token_abc");
    }

    [Fact(DisplayName = "Given a claim without proxy fields, when mapped, then the pi environment is left untouched (null)")]
    public void MapNothingFromAClaimWithoutAMint()
    {
        var claimed = NewClaim();

        var environment = PiEnvironment.FromClaim(claimed);

        environment.ShouldBeNull();
    }

    private static ClaimedWorkItemResponse NewClaim(string? ProxyBaseUrl = null, string? VirtualKey = null)
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
