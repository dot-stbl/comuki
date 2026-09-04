using Comuki.Modules.Identity.Application.Oidc;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// Authorize URL builder: PKCE S256, state, scope, redirect_uri —
/// every OAuth2 param the framework also sets, but produced from
/// primitives (no challenge method 'plain', no openid scope dropped,
/// no extra response_type).
/// </summary>
public sealed class OidcAuthorizationUrlBuilderShould
{
    [Fact(DisplayName = "Given a provider config, when Build is called, then the URL carries every OAuth2 param the code-flow expects")]
    public void BuildCarriesCodeFlowParams()
    {
        var provider = new OidcProviderOptions
        {
            Name = "keycloak",
            Authority = "https://kc.example.com/realms/comuki",
            ClientId = "comuki-dashboard",
            ClientSecretEnv = "COMUKI_OIDC_CLIENT_SECRET",
        };
        var authorize = new Uri("https://kc.example.com/realms/comuki/protocol/openid-connect/auth");
        var redirectUri = "https://app.example.com/api/v1/auth/oidc/callback";
        var state = "11111111-2222-3333-4444-555555555555";
        var challenge = "challenge-abcdef";

        var url = OidcAuthorizationUrlBuilder.Build(
            provider,
            authorize,
            provider.ClientId,
            redirectUri,
            "openid profile email",
            state,
            challenge);

        url.Query.ShouldContain("response_type=code");
        url.Query.ShouldContain("client_id=comuki-dashboard");
        url.Query.ShouldContain("redirect_uri=https%3A%2F%2Fapp.example.com%2Fapi%2Fv1%2Fauth%2Foidc%2Fcallback");
        url.Query.ShouldContain("scope=openid%20profile%20email");
        url.Query.ShouldContain("state=11111111-2222-3333-4444-555555555555");
        url.Query.ShouldContain("code_challenge=challenge-abcdef");
        url.Query.ShouldContain("code_challenge_method=S256");
    }

    [Fact(DisplayName = "Given a value with reserved URL chars, when Build is called, then values are percent-encoded once")]
    public void BuildEncodesValuesOnce()
    {
        var provider = new OidcProviderOptions
        {
            Name = "keycloak",
            Authority = "https://kc.example.com/realms/comuki",
            ClientId = "client-with-spaces and+/=",
            ClientSecretEnv = "X",
        };
        var authorize = new Uri("https://kc.example.com/realms/comuki/protocol/openid-connect/auth");

        var url = OidcAuthorizationUrlBuilder.Build(
            provider,
            authorize,
            provider.ClientId,
            "https://app.example.com/cb",
            "openid profile email",
            "11111111-2222-3333-4444-555555555555",
            "challenge");

        url.Query.ShouldContain("client_id=client-with-spaces%20and%2B%2F%3D");
        url.Query.ShouldNotContain("client_id=client-with-spaces and+/=");
    }
}

/// <summary>
/// PKCE pair generation: 43-char verifier (no padding), S256 challenge
/// (sha256 + base64url, no padding), and the math matches.
/// </summary>
public sealed class OidcPkceShould
{
    [Fact(DisplayName = "Given fresh pair, when Generate runs, then both are base64url-no-padding and the challenge is the SHA-256 of the verifier")]
    public void PairMatchesS256Spec()
    {
        var pair = OidcPkce.Generate();

        pair.Verifier.Length.ShouldBe(43);
        pair.Challenge.Length.ShouldBe(43);

        // base64url alphabet + no padding (43 chars = 32 bytes encoded)
        pair.Verifier.ShouldAllBe(static c => IsBase64UrlChar(c));
        pair.Challenge.ShouldAllBe(static c => IsBase64UrlChar(c));

        var expected = OidcPkce.ComputeS256Challenge(pair.Verifier);
        expected.ShouldBe(pair.Challenge);
    }

    [Fact(DisplayName = "Given two fresh pairs, when Generate runs twice, then verifiers are different")]
    public void TwoCallsGiveDifferentVerifiers()
    {
        var first = OidcPkce.Generate();
        var second = OidcPkce.Generate();

        first.Verifier.ShouldNotBe(second.Verifier);
        first.Challenge.ShouldNotBe(second.Challenge);
    }

    private static bool IsBase64UrlChar(char c)
    {
        return c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_';
    }
}
