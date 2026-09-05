using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Subjects;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Auth;

/// <summary>
/// Object-axis semantics against the booted host (issue #12 tail): the
/// global subject-scope query filters confine project-scoped subjects to
/// their own projects — out-of-scope ids answer 404, lists narrow, runs
/// and work items follow their project — while platform-scope subjects
/// keep seeing everything, and anonymous callers get the fail-closed
/// empty answer instead of a leak.
/// </summary>
[Collection(nameof(AuthIntegrationCollection))]
public sealed class ProjectScopeShould(HostAuthServer server) : IClassFixture<HostAuthServer>
{
    private const string MemberPassword = "user-pass-123";

    private static async Task<JsonElement> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{email}: {await response.Content.ReadAsStringAsync()}");

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<IReadOnlyList<string>> ListProjectIdsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/projects?includeArchived=false", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        return [.. payload.EnumerateArray().Select(static project => project.GetProperty("id").GetProperty("value").GetString()!)];
    }

    [Fact(DisplayName = "Given a member on project A, when listing projects, then only A is visible while the platform admin sees both")]
    public async Task ConfineProjectListToTheSubjectsScopeAsync()
    {
        var projectA = await server.CreateProjectAsync("Alpha", "alpha");
        var projectB = await server.CreateProjectAsync("Beta", "beta");
        var member = await server.CreateUserAsync("scoped-member@comuki.test");
        await server.GrantProjectRoleAsync(RoleSubject.ForUser(member.Id), Role.Member, projectA.Id);

        using var memberClient = server.CreateBrowserClient();
        await LoginAsync(memberClient, "scoped-member@comuki.test", MemberPassword);
        var memberIds = await ListProjectIdsAsync(memberClient);

        using var adminClient = server.CreateBrowserClient();
        await LoginAsync(adminClient, HostAuthServer.BootstrapEmail, HostAuthServer.BootstrapPassword);
        var adminIds = await ListProjectIdsAsync(adminClient);

        memberIds.ShouldBe([projectA.Id.ToString()]);
        adminIds.ShouldContain(projectA.Id.ToString());
        adminIds.ShouldContain(projectB.Id.ToString());
    }

    [Fact(DisplayName = "Given a member on project A, when getting project A then 200, and when getting project B then 404 problem")]
    public async Task Answer404ForOutOfScopeProjectIdsAsync()
    {
        var projectA = await server.CreateProjectAsync("Gamma", "gamma");
        var projectB = await server.CreateProjectAsync("Delta", "delta");
        var member = await server.CreateUserAsync("scoped-getter@comuki.test");
        await server.GrantProjectRoleAsync(RoleSubject.ForUser(member.Id), Role.Member, projectA.Id);

        using var client = server.CreateBrowserClient();
        await LoginAsync(client, "scoped-getter@comuki.test", MemberPassword);

        var own = await client.GetAsync($"/api/v1/projects/{projectA.Id}", TestContext.Current.CancellationToken);
        var foreign = await client.GetAsync($"/api/v1/projects/{projectB.Id}", TestContext.Current.CancellationToken);

        own.StatusCode.ShouldBe(HttpStatusCode.OK);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreign.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact(DisplayName = "Given an anonymous caller, when listing projects, then the answer is the fail-closed empty list")]
    public async Task AnonymousListIsEmptyAsync()
    {
        await server.CreateProjectAsync("Epsilon", "epsilon");

        using var client = server.CreateApiKeyClient();
        var ids = await ListProjectIdsAsync(client);

        ids.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given runs in projects A and B, when the member's scope reads runs and work items, then only project A's rows are visible")]
    public async Task FilterRunsAndWorkItemsByProjectScopeAsync()
    {
        var projectA = await server.CreateProjectAsync("Zeta", "zeta");
        var projectB = await server.CreateProjectAsync("Eta", "eta");
        var runA = await server.SeedRunWithItemAsync(projectA.Id);
        var runB = await server.SeedRunWithItemAsync(projectB.Id);
        var member = await server.CreateUserAsync("run-reader@comuki.test");
        await server.GrantProjectRoleAsync(RoleSubject.ForUser(member.Id), Role.Viewer, projectA.Id);

        var visibleRuns = await server.VisibleRunsAsync(RoleSubject.ForUser(member.Id));
        var visibleItems = await server.VisibleWorkItemsAsync(RoleSubject.ForUser(member.Id));
        var adminRuns = await server.VisibleRunsAsync(RoleSubject.ForUser((await server.FindUserAsync(HostAuthServer.BootstrapEmail))!.Id));

        visibleRuns.ShouldBe([runA.Value]);
        visibleItems.ShouldHaveSingleItem();
        adminRuns.ShouldContain(runA.Value);
        adminRuns.ShouldContain(runB.Value);
    }
}
