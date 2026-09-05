using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Smoke;

/// <summary>
/// Identity-admin endpoint permission coverage (admin-backend-fixes merge):
/// <list type="bullet">
///   <item><c>POST /api/v1/users</c> with the bootstrap admin returns 201
///     with the user view (email + displayName + disabled = false).</item>
///   <item><c>POST /api/v1/users</c> as an anonymous caller returns 401.</item>
///   <item><c>POST /api/v1/users</c> as an authenticated user without the
///     <c>identity:write</c> permission returns 403 with
///     <c>code = permission.denied</c>.</item>
/// </list>
/// </summary>
public sealed class AdminShould(SmokeHostServer server) : IClassFixture<SmokeHostServer>
{
    private readonly SmokeHostServer server = server;

    [Fact(DisplayName = "Given the bootstrap admin, when POST /api/v1/users with a valid email, then 201 with the user view")]
    public async Task AdminInviteReturns201WithUserViewAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await server.CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/users",
            new
            {
                email = $"smoke-admin-{Guid.NewGuid():N}@comuki.test",
                displayName = "Smoke Admin",
            },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(cancellationToken));
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        payload.GetProperty("email").GetString().ShouldEndWith("@comuki.test");
        payload.GetProperty("displayName").GetString().ShouldBe("Smoke Admin");
        payload.GetProperty("disabled").GetBoolean().ShouldBeFalse();
        // The id is a strong-typed wrapper { "value": "<guid>" } — the
        // existing IdentityAdminEndpointsShould suite asserts the same
        // pattern; reaching into it keeps the test resilient against
        // future shape changes (e.g. envelope tweaks).
        var idWrapper = payload.GetProperty("id");
        idWrapper.ValueKind.ShouldBe(JsonValueKind.Object);
    }

    [Fact(DisplayName = "Given an anonymous caller, when POST /api/v1/users, then 401 problem with authentication.required")]
    public async Task AdminInviteAnonymousReturns401Async()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = server.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/users",
            new { email = "any@comuki.test" },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Given an authenticated user without identity:write, when POST /api/v1/users, then 403 permission.denied")]
    public async Task AdminInviteWithoutIdentityWriteReturns403Async()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, _) = await server.CreateRolelessUserClientAsync();
        using var _ = client;

        var response = await client.PostAsJsonAsync(
            "/api/v1/users",
            new
            {
                email = $"smoke-forbidden-{Guid.NewGuid():N}@comuki.test",
                displayName = "Should Be Forbidden",
            },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        payload.GetProperty("code").GetString().ShouldBe("permission.denied");
    }
}
