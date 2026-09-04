using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Oidc;

/// <summary>
/// OIDC e2e against real Keycloak (issue #12 tail). Covered layers:
/// discovery document, the authorize redirect our OIDC handler builds
/// (correct issuer, client, PKCE S256, callback on the versioned API
/// surface), the password-grant token endpoint, the userinfo claims, and
/// the linker resolving those claims to local account rows. The browser
/// callback exchange itself is out of scope (see <see cref="HostOidcServer"/>).
/// </summary>
public sealed class OidcKeycloakShould(HostOidcServer server) : IClassFixture<HostOidcServer>
{
    private async Task<JsonElement> GetUserClaimsAsync(string accessToken)
    {
        using var userInfoClient = new HttpClient();
        using var userInfo = new HttpRequestMessage(HttpMethod.Get, $"{server.Authority}/protocol/openid-connect/userinfo");
        userInfo.Headers.Authorization = new("Bearer", accessToken);

        var response = await userInfoClient.SendAsync(userInfo, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<string> PasswordGrantTokenAsync()
    {
        using var tokenClient = new HttpClient();
        using var response = await tokenClient.PostAsync(
            $"{server.Authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = HostOidcServer.ClientId,
                ["username"] = HostOidcServer.TestUsername,
                ["password"] = HostOidcServer.TestPassword,
                ["scope"] = "openid profile email",
            }),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        return payload.GetProperty("access_token").GetString()!;
    }

    [Fact(DisplayName = "Given the keycloak realm, when fetching the discovery document, then it answers with the matching issuer")]
    public async Task DiscoveryDocumentMatchesTheRealmAsync()
    {
        using var client = server.CreateClient();

        var payload = await client.GetFromJsonAsync<JsonElement>(
            $"{server.Authority}/.well-known/openid-configuration",
            TestContext.Current.CancellationToken);

        payload.GetProperty("issuer").GetString().ShouldBe(server.Authority);
        payload.GetProperty("authorization_endpoint").GetString().ShouldStartWith($"{server.Authority}/protocol/openid-connect/auth");
        payload.GetProperty("token_endpoint").GetString().ShouldStartWith($"{server.Authority}/protocol/openid-connect/token");
    }

    [Fact(DisplayName = "Given the configured provider, when GET /auth/oidc/keycloak/start, then 302 to keycloak authorize with the dashboard client, PKCE S256 and the unified callback path")]
    public async Task StartRedirectsToKeycloakAuthorizeAsync()
    {
        using var client = server.CreateNoRedirectClient();

        var response = await client.GetAsync("/api/v1/auth/oidc/keycloak/start", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        var location = response.Headers.Location?.ToString();
        location.ShouldNotBeNullOrEmpty();
        location.ShouldStartWith($"{server.Authority}/protocol/openid-connect/auth");
        location.ShouldContain($"client_id={HostOidcServer.ClientId}");
        location.ShouldContain("code_challenge_method=S256");
        location.ShouldContain("code_challenge=");
        // The manual code-flow uses a single unified callback (no
        // provider in the path) — IdPs register this absolute URL.
        location.ShouldContain($"redirect_uri={Uri.EscapeDataString($"{client.BaseAddress}api/v1/auth/oidc/callback")}");
    }

    [Fact(DisplayName = "Given the configured provider, when GET /auth/oidc/keycloak/start with a returnTo query, then the authorize URL carries the state")]
    public async Task StartPropagatesReturnToAsync()
    {
        using var client = server.CreateNoRedirectClient();

        var response = await client.GetAsync("/api/v1/auth/oidc/keycloak/start?returnTo=%2Fruns", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        var location = response.Headers.Location?.ToString();
        location.ShouldNotBeNullOrEmpty();
        location.ShouldContain("state=");
    }

    [Fact(DisplayName = "Given the test user, when the password grant hits the token endpoint, then userinfo answers the subject and email claims")]
    public async Task PasswordGrantAndUserinfoAnswerClaimsAsync()
    {
        var accessToken = await PasswordGrantTokenAsync();

        var claims = await GetUserClaimsAsync(accessToken);

        claims.GetProperty("sub").GetString().ShouldNotBeNullOrEmpty();
        claims.GetProperty("email").GetString().ShouldBe(HostOidcServer.TestEmail);
        claims.GetProperty("email_verified").GetBoolean().ShouldBeTrue();
    }

    [Fact(DisplayName = "Given real userinfo claims, when the linker runs twice, then it provisions once and links the same account after")]
    public async Task LinkerProvisionsOnceAgainstRealClaimsAsync()
    {
        var claims = await GetUserClaimsAsync(await PasswordGrantTokenAsync());
        var subject = claims.GetProperty("sub").GetString()!;
        var name = claims.TryGetProperty("name", out var nameClaim) ? nameClaim.GetString() : null;

        var first = await server.LinkAsync(subject, HostOidcServer.TestEmail, name, TestContext.Current.CancellationToken);
        var second = await server.LinkAsync(subject, HostOidcServer.TestEmail, name, TestContext.Current.CancellationToken);

        first.Created.ShouldBeTrue("a brand-new identity must provision a password-less account");
        first.User.Email.ShouldBe(HostOidcServer.TestEmail);
        first.User.Id.ShouldBe(second.User.Id);
        second.Created.ShouldBeFalse("the stored link wins on the second pass");
    }
}
