using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comuki.Host.Auth.Models;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Auth;

/// <summary>
/// Identity admin endpoints end-to-end (issues #31-#37): invite / grant /
/// revoke / link / key under the real host composition. One cookie
/// session per test class is established in <see cref="InitializeAsync"/>
/// so the login rate-limit partition does not throttle the burst —
/// every test then reuses that cookie.
/// </summary>
[Collection(nameof(AuthIntegrationCollection))]
public sealed class IdentityAdminEndpointsShould(HostAuthServer server) : IClassFixture<HostAuthServer>, IAsyncLifetime
{
    private HttpClient loggedInClient = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        loggedInClient = server.CreateBrowserClient();
        var response = await loggedInClient.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = HostAuthServer.BootstrapEmail, password = HostAuthServer.BootstrapPassword },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        loggedInClient?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact(DisplayName = "Given the bootstrap admin, when POST /api/v1/users with an email, then 201 with the account view")]
    public async Task InviteUserPersistsAsync()
    {
        var response = await loggedInClient.PostAsJsonAsync(
            "/api/v1/users",
            new InviteUserRequest { Email = $"invite-{Guid.NewGuid():N}@comuki.test", DisplayName = "Invited" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        view.GetProperty("email").GetString().ShouldEndWith("@comuki.test");
        view.GetProperty("displayName").GetString().ShouldBe("Invited");
        view.GetProperty("disabled").GetBoolean().ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an invalid email, when POST /api/v1/users, then 400 validation problem")]
    public async Task InviteUserWithBadEmailReturns400Async()
    {
        var response = await loggedInClient.PostAsJsonAsync(
            "/api/v1/users",
            new InviteUserRequest { Email = "not-an-email" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "Given an unauthenticated caller, when POST /api/v1/users, then 401 authentication.required")]
    public async Task InviteUserWithoutAuthReturns401Async()
    {
        using var client = server.CreateApiKeyClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/users",
            new InviteUserRequest { Email = "any@comuki.test" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Given a free user, when POST /api/v1/grants with platform-scope, then 201 with the assignment view")]
    public async Task GrantPlatformRolePersistsAsync()
    {
        var user = await server.CreateUserAsync($"grantee-{Guid.NewGuid():N}@comuki.test");

        var response = await loggedInClient.PostAsJsonAsync(
            "/api/v1/grants",
            new GrantRoleRequest { UserId = user.Id.Value, Role = "member" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        view.GetProperty("role").GetString().ShouldBe("member");
        view.GetProperty("scopeLevel").GetString().ShouldBe("platform");
        view.GetProperty("isActive").GetBoolean().ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a free user, when POST /api/v1/grants with project-scope, then 201 with project scope")]
    public async Task GrantProjectRolePersistsAsync()
    {
        var project = await server.CreateProjectAsync("p", $"p-{Guid.NewGuid():N}");
        var user = await server.CreateUserAsync($"grantee-{Guid.NewGuid():N}@comuki.test");

        var response = await loggedInClient.PostAsJsonAsync(
            "/api/v1/grants",
            new GrantRoleRequest { UserId = user.Id.Value, Role = "member", ProjectId = project.Id.Value },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        view.GetProperty("scopeLevel").GetString().ShouldBe("project");
        view.GetProperty("scopeProjectId").GetString().ShouldBe(project.Id.Value.ToString());
    }

    [Fact(DisplayName = "Given an unknown role, when POST /api/v1/grants, then 400 validation problem")]
    public async Task GrantUnknownRoleReturns400Async()
    {
        var user = await server.CreateUserAsync($"grantee-{Guid.NewGuid():N}@comuki.test");

        var response = await loggedInClient.PostAsJsonAsync(
            "/api/v1/grants",
            new GrantRoleRequest { UserId = user.Id.Value, Role = "overlord" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "Given an active grant, when POST /api/v1/grants/{grantId}/revoke, then 200 with revokedAt set")]
    public async Task RevokeGrantPersistsAsync()
    {
        var user = await server.CreateUserAsync($"grantee-{Guid.NewGuid():N}@comuki.test");

        var grant = await loggedInClient.PostAsJsonAsync(
            "/api/v1/grants",
            new GrantRoleRequest { UserId = user.Id.Value, Role = "member" },
            TestContext.Current.CancellationToken);
        grant.StatusCode.ShouldBe(HttpStatusCode.Created);
        var grantView = await grant.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        // Strong-typed id wrappers serialize as { "value": "…" } on the
        // wire — reach into the wrapper to recover the Guid string.
        var grantId = Guid.Parse(grantView.GetProperty("id").GetProperty("value").GetString()!);

        var revoke = await loggedInClient.PostAsync(
            $"/api/v1/grants/{grantId}/revoke",
            content: null,
            TestContext.Current.CancellationToken);

        revoke.StatusCode.ShouldBe(HttpStatusCode.OK);
        var revoked = await revoke.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        revoked.GetProperty("isActive").GetBoolean().ShouldBeFalse();
        revoked.GetProperty("revokedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    [Fact(DisplayName = "Given a user, when POST /api/v1/keys, then 201 with the secret shown once")]
    public async Task IssueApiKeyReturnsSecretOnceAsync()
    {
        var user = await server.CreateUserAsync($"key-owner-{Guid.NewGuid():N}@comuki.test");

        var response = await loggedInClient.PostAsJsonAsync(
            "/api/v1/keys",
            new CreateApiKeyRequest { UserId = user.Id.Value, Label = "ci-token" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var secret = payload.GetProperty("secret").GetString();
        secret.ShouldStartWith("ck_");
        payload.GetProperty("prefix").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact(DisplayName = "Given an issued key, when POST /api/v1/keys/{keyId}/revoke, then 200 with status=revoked")]
    public async Task RevokeApiKeyPersistsAsync()
    {
        var user = await server.CreateUserAsync($"key-owner-{Guid.NewGuid():N}@comuki.test");

        var issue = await loggedInClient.PostAsJsonAsync(
            "/api/v1/keys",
            new CreateApiKeyRequest { UserId = user.Id.Value, Label = "revoke-me" },
            TestContext.Current.CancellationToken);
        issue.StatusCode.ShouldBe(HttpStatusCode.Created);
        var keyView = await issue.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        // IssuedApiKeyResponse wraps the api key id as a Guid — wire form
        // is the bare string, no strong-typed wrapper.
        var keyId = Guid.Parse(keyView.GetProperty("keyId").GetString()!);

        var revoke = await loggedInClient.PostAsync(
            $"/api/v1/keys/{keyId}/revoke",
            content: null,
            TestContext.Current.CancellationToken);

        revoke.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = await revoke.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        view.GetProperty("isActive").GetBoolean().ShouldBeFalse();
        view.GetProperty("revokedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    [Fact(DisplayName = "Given a free user, when POST /api/v1/users/{userId}/oidc-link, then 201 with the link view")]
    public async Task LinkOidcSubjectPersistsAsync()
    {
        var user = await server.CreateUserAsync($"linkable-{Guid.NewGuid():N}@comuki.test");

        var response = await loggedInClient.PostAsJsonAsync(
            $"/api/v1/users/{user.Id.Value}/oidc-link",
            new LinkOidcRequest { Provider = "Keycloak", SubjectId = "sub-abc-123" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        view.GetProperty("provider").GetString().ShouldBe("keycloak");
        view.GetProperty("subject").GetString().ShouldBe("sub-abc-123");
    }

    [Fact(DisplayName = "Given an already-bound (provider, subject), when POST /api/v1/users/{userId}/oidc-link again, then 500 (semantic duplicate)")]
    public async Task LinkOidcSubjectDuplicateReturnsErrorAsync()
    {
        var user = await server.CreateUserAsync($"linkable-{Guid.NewGuid():N}@comuki.test");

        var first = await loggedInClient.PostAsJsonAsync(
            $"/api/v1/users/{user.Id.Value}/oidc-link",
            new LinkOidcRequest { Provider = "Keycloak", SubjectId = "sub-dup-456" },
            TestContext.Current.CancellationToken);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await loggedInClient.PostAsJsonAsync(
            $"/api/v1/users/{user.Id.Value}/oidc-link",
            new LinkOidcRequest { Provider = "Keycloak", SubjectId = "sub-dup-456" },
            TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [Fact(DisplayName = "Given an enabled user, when PATCH /api/v1/users/{userId} disabled=true, then 200 with disabled=true")]
    public async Task SetUserDisabledPersistsAsync()
    {
        var user = await server.CreateUserAsync($"toggle-{Guid.NewGuid():N}@comuki.test");

        var response = await loggedInClient.PatchAsync(
            $"/api/v1/users/{user.Id.Value}",
            JsonContent.Create(new SetUserDisabledRequest { Disabled = true }),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        view.GetProperty("disabled").GetBoolean().ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a disabled user, when PATCH /api/v1/users/{userId} disabled=false, then 200 with disabled=false")]
    public async Task SetUserDisabledUnsetsAsync()
    {
        var user = await server.CreateUserAsync($"toggle-{Guid.NewGuid():N}@comuki.test");

        var first = await loggedInClient.PatchAsync(
            $"/api/v1/users/{user.Id.Value}",
            JsonContent.Create(new SetUserDisabledRequest { Disabled = true }),
            TestContext.Current.CancellationToken);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await loggedInClient.PatchAsync(
            $"/api/v1/users/{user.Id.Value}",
            JsonContent.Create(new SetUserDisabledRequest { Disabled = false }),
            TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = await second.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        view.GetProperty("disabled").GetBoolean().ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an unknown user id, when PATCH /api/v1/users/{userId}, then 500 (semantic miss)")]
    public async Task SetUserDisabledMissingUserReturnsErrorAsync()
    {
        var response = await loggedInClient.PatchAsync(
            $"/api/v1/users/{Guid.NewGuid()}",
            JsonContent.Create(new SetUserDisabledRequest { Disabled = true }),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }
}
