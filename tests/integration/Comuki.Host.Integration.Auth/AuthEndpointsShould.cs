using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Subjects;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Auth;

/// <summary>
/// End-to-end auth scenarios against the booted host composition:
/// login/logout/me over the cookie session, permission demands on the
/// control-plane endpoints (401 anonymous, 403 without the key),
/// API-key bearer flows resolving to the key's own subject, and the
/// bootstrap admin's idempotence.
/// </summary>
[Collection(nameof(AuthIntegrationCollection))]
public sealed class AuthEndpointsShould(HostAuthServer server) : IClassFixture<HostAuthServer>
{
    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        return payload.GetProperty("code").GetString()!;
    }

    private static async Task<JsonElement> LoginAsync(
        HttpClient client,
        string email,
        string password = "user-pass-123")
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact(DisplayName = "Given a running host, when GET /health, then it answers 200 anonymously")]
    public async Task HealthStaysAnonymousAsync()
    {
        using var client = server.CreateApiKeyClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "Given the bootstrap admin, when login with its credentials, then 200 with the account and a session cookie")]
    public async Task LoginWithBootstrapAdminAsync()
    {
        using var client = server.CreateBrowserClient();
        var login = await LoginAsync(client, HostAuthServer.BootstrapEmail, HostAuthServer.BootstrapPassword);

        login.GetProperty("userId").GetString().ShouldNotBeNullOrEmpty();
        login.GetProperty("email").GetString().ShouldBe(HostAuthServer.BootstrapEmail);
    }

    [Fact(DisplayName = "Given the bootstrap admin, when login with a wrong password, then 401 problem with the stable invalid-credentials code")]
    public async Task LoginWithWrongPasswordReturns401ProblemAsync()
    {
        using var client = server.CreateApiKeyClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = HostAuthServer.BootstrapEmail, password = "definitely-wrong-1" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await ProblemCodeAsync(response)).ShouldBe("auth.invalid_credentials");
    }

    [Fact(DisplayName = "Given a malformed email, when login, then 400 validation problem")]
    public async Task LoginWithMalformedEmailReturns400Async()
    {
        using var client = server.CreateApiKeyClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = "not-an-email", password = "whatever-pass" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "Given a cookie session, when GET /auth/me, then the subject, its roles and effective permissions are returned")]
    public async Task MeWithCookieReturnsSubjectAndPermissionsAsync()
    {
        using var client = server.CreateBrowserClient();
        var login = await LoginAsync(client, HostAuthServer.BootstrapEmail, HostAuthServer.BootstrapPassword);
        var userId = login.GetProperty("userId").GetString();

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me", TestContext.Current.CancellationToken);

        me.GetProperty("userId").GetString().ShouldBe(userId);
        me.GetProperty("subjectType").GetString().ShouldBe("user");
        me.GetProperty("email").GetString().ShouldBe(HostAuthServer.BootstrapEmail);
        me.GetProperty("roles").EnumerateArray().Select(static role => role.GetString()).ShouldBe(["platform-admin"]);
        var platform = me.GetProperty("permissions").GetProperty("platform");
        platform.EnumerateArray().Select(static key => key.GetString()).ShouldContain("plan:read");
        platform.EnumerateArray().Select(static key => key.GetString()).ShouldContain("chat:use");
    }

    [Fact(DisplayName = "Given no session, when GET /auth/me, then 401 problem with authentication.required")]
    public async Task MeWithoutSessionReturns401Async()
    {
        using var client = server.CreateApiKeyClient();

        var response = await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(response)).ShouldBe("authentication.required");
    }

    [Fact(DisplayName = "Given an anonymous caller, when GET /profiles, then 401 problem with authentication.required")]
    public async Task ProfilesAnonymousReturns401Async()
    {
        using var client = server.CreateApiKeyClient();

        var response = await client.GetAsync("/profiles", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(response)).ShouldBe("authentication.required");
    }

    [Fact(DisplayName = "Given an authenticated user without assignments, when GET /profiles, then 403 problem with permission.denied")]
    public async Task AuthenticatedWithoutAssignmentsGets403OnProfilesAsync()
    {
        await server.CreateUserAsync("roleless@comuki.test");
        using var client = server.CreateBrowserClient();
        var login = await LoginAsync(client, "roleless@comuki.test");
        login.GetProperty("userId").GetString().ShouldNotBeNullOrEmpty();

        var response = await client.GetAsync("/profiles", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(response)).ShouldBe("permission.denied");
    }

    [Fact(DisplayName = "Given an approver (plan:read but no chat:use), when GET /profiles then 200, and when GET /chat-commands then 403 permission.denied")]
    public async Task ApproverReadsProfilesButNotChatCommandsAsync()
    {
        var approver = await server.CreateUserAsync("approver@comuki.test");
        using var client = server.CreateBrowserClient();
        var login = await LoginAsync(client, "approver@comuki.test");
        var userId = Guid.Parse(login.GetProperty("userId").GetString()!);
        userId.ShouldBe(approver.Id.Value);
        await server.GrantPlatformRoleAsync(new RoleSubject(SubjectType.User, userId), Role.Approver);

        var profiles = await client.GetAsync("/profiles", TestContext.Current.CancellationToken);
        var chatCommands = await client.GetAsync("/chat-commands", TestContext.Current.CancellationToken);

        profiles.StatusCode.ShouldBe(HttpStatusCode.OK);
        chatCommands.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(chatCommands)).ShouldBe("permission.denied");
    }

    [Fact(DisplayName = "Given an API key with a member role on the key subject, when bearer calls /chat-commands and /auth/me, then the key's own permissions and subject answer")]
    public async Task ApiKeyCallAuthorizesThroughKeyAssignmentsAsync()
    {
        var owner = await server.CreateUserAsync("key-owner@comuki.test");
        var key = await server.IssueApiKeyAsync(owner.Id);
        await server.GrantPlatformRoleAsync(RoleSubject.ForApiKey(key.Id), Role.Member);

        using var client = server.CreateApiKeyClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", key.PlaintextToken);

        var chatCommands = await client.GetAsync("/chat-commands", TestContext.Current.CancellationToken);

        chatCommands.StatusCode.ShouldBe(HttpStatusCode.OK);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me", TestContext.Current.CancellationToken);

        me.GetProperty("userId").GetString().ShouldBe(owner.Id.Value.ToString());
        me.GetProperty("subjectType").GetString().ShouldBe("api-key");
        me.GetProperty("subjectId").GetString().ShouldBe(key.Id.Value.ToString());
        me.GetProperty("roles").EnumerateArray().Select(static role => role.GetString()).ShouldBe(["member"]);
    }

    [Fact(DisplayName = "Given a session, when logout then GET /auth/me, then 204 followed by 401 - the cookie is gone")]
    public async Task LogoutClearsTheSessionAsync()
    {
        using var client = server.CreateBrowserClient();
        await LoginAsync(client, HostAuthServer.BootstrapEmail, HostAuthServer.BootstrapPassword);

        var logout = await client.PostAsync("/api/v1/auth/logout", content: null, TestContext.Current.CancellationToken);
        var me = await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Given an already-bootstrapped admin, when the seeder runs again, then nothing is duplicated")]
    public async Task BootstrapAdminIsIdempotentAsync()
    {
        await server.RunBootstrapSeederAgainAsync();

        var bootstrap = await server.FindUserAsync(HostAuthServer.BootstrapEmail);
        bootstrap.ShouldNotBeNull();
        (await server.ActiveRoleKeysAsync(RoleSubject.ForUser(bootstrap.Id))).ShouldBe(["platform-admin"]);
    }

    [Fact(DisplayName = "Given an unknown oidc provider, when GET /auth/oidc/{provider}/start, then 404 problem")]
    public async Task OidcStartWithUnknownProviderReturns404Async()
    {
        using var client = server.CreateApiKeyClient();

        var response = await client.GetAsync("/api/v1/auth/oidc/nowhere/start", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(response)).ShouldBe("auth.oidc_provider_not_found");
    }
}
