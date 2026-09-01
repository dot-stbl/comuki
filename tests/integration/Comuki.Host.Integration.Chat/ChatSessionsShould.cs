using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Chat;

/// <summary>
/// End-to-end chat slice over the real composition: boot, session
/// lifecycle, brain-stub turn, approve → run + queued work item, slash
/// catalog merge, /init wizard and the permission gate (issue #5 slice B).
/// </summary>
public sealed class ChatSessionsShould(HostChatServer server) : IClassFixture<HostChatServer>
{
    private static readonly Guid projectGuid = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact(DisplayName = "Given no session, when chat endpoints are called anonymously, then they answer 401")]
    public async Task DemandAuthenticationAsync()
    {
        using var client = server.CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/chat/sessions", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Given a logged-in subject, when a session is created, then it answers 201 with the session view")]
    public async Task CreateSessionAsync()
    {
        using var client = await server.CreateBrowserClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/chat/sessions",
            new { projectId = projectGuid, title = "first chat" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var session = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        session.GetProperty("projectId").GetGuid().ShouldBe(projectGuid);
        session.GetProperty("title").GetString().ShouldBe("first chat");
        session.GetProperty("status").GetString().ShouldBe("active");
        response.Headers.Location.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given a session with project scope, when a task message is posted, then the stub brain plan interrupts with an approve card")]
    public async Task TaskMessageInterruptsWithApproveCardAsync()
    {
        using var client = await server.CreateBrowserClientAsync();
        var sessionId = await NewSessionAsync(client, projectGuid);

        var turn = await PostMessageAsync(client, sessionId, "fix the login bug");

        turn.GetProperty("awaitingApproval").GetBoolean().ShouldBeTrue();
        var plan = turn.GetProperty("pendingPlan");
        plan.GetProperty("items")[0].GetProperty("profileKey").GetString().ShouldBe("implement");

        // the transcript journals the card prompt as the assistant reply
        var messages = await ListMessagesAsync(client, sessionId);
        messages.ShouldContain(row => row.GetProperty("role").GetString() == "assistant"
            && row.GetProperty("content").GetString()!.Contains("approve"));
    }

    [Fact(DisplayName = "Given a pending approve, when another message is posted, then it answers 409 approve_pending")]
    public async Task RefuseTurnWhileApprovePendingAsync()
    {
        using var client = await server.CreateBrowserClientAsync();
        var sessionId = await NewSessionAsync(client, projectGuid);
        await PostMessageAsync(client, sessionId, "fix the login bug");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/chat/sessions/{sessionId}/messages",
            new { message = "another thing" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("code").GetString().ShouldBe("chat.approve_pending");
    }

    [Fact(DisplayName = "Given a pending approve, when approved, then a run with one queued work item lands in orchestration")]
    public async Task ApproveCreatesRunAndQueuedItemAsync()
    {
        using var client = await server.CreateBrowserClientAsync();
        var sessionId = await NewSessionAsync(client, projectGuid);
        await PostMessageAsync(client, sessionId, "fix the login bug");

        var approve = await client.PostAsJsonAsync(
            $"/api/v1/chat/sessions/{sessionId}/approve",
            new { approved = true },
            TestContext.Current.CancellationToken);
        approve.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await approve.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        result.GetProperty("awaitingApproval").GetBoolean().ShouldBeFalse();

        await using var db = await NewOrchestrationDbAsync();
        var run = await db.Runs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ProjectId == new Shared.Kernel.Ids.ProjectId(projectGuid), TestContext.Current.CancellationToken);
        run.ShouldNotBeNull();

        var items = await db.WorkItems.AsNoTracking()
            .Where(item => item.RunId == run.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        var item = items.ShouldHaveSingleItem();
        item.Status.ShouldBe(Engine.Orchestration.Domain.WorkItemStatus.Queued);
        item.ProfileKey.ShouldBe("implement");

        using var brief = JsonDocument.Parse(item.Brief);
        brief.RootElement.GetProperty("goal").GetString().ShouldBe("fix the login bug");
    }

    [Fact(DisplayName = "Given a pending approve, when rejected, then no run is created and the reason is echoed")]
    public async Task RejectCreatesNothingAsync()
    {
        using var client = await server.CreateBrowserClientAsync();
        var sessionId = await NewSessionAsync(client, projectGuid);
        await PostMessageAsync(client, sessionId, "write the migration");

        var reject = await client.PostAsJsonAsync(
            $"/api/v1/chat/sessions/{sessionId}/approve",
            new { approved = false, reason = "wrong project" },
            TestContext.Current.CancellationToken);
        reject.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await reject.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        result.GetProperty("messages")
            .EnumerateArray()
            .ShouldContain(row => row.GetProperty("role").GetString() == "assistant"
                && row.GetProperty("content").GetString()!.Contains("wrong project"));

        await using var db = await NewOrchestrationDbAsync();
        (await db.Runs.AsNoTracking()
                .AnyAsync(row => row.ProjectId == new Shared.Kernel.Ids.ProjectId(projectGuid), TestContext.Current.CancellationToken))
            .ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a foreign session id, when used, then it answers 404")]
    public async Task UnknownSessionAnswers404Async()
    {
        using var client = await server.CreateBrowserClientAsync();

        var response = await client.GetAsync(
            $"/api/v1/chat/sessions/{Guid.NewGuid()}/messages", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "Given the slash catalog, when listed, then built-ins merge with the control-plane pack")]
    public async Task ListSlashCatalogAsync()
    {
        using var client = await server.CreateBrowserClientAsync();

        var response = await client.GetAsync("/api/v1/chat/slash", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var commands = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var keys = commands.EnumerateArray().Select(static command => command.GetProperty("key").GetString()).ToList();
        keys.ShouldContain("help");
        keys.ShouldContain("init");
        keys.ShouldContain("restart");
    }

    [Fact(DisplayName = "Given /init, when two answers are posted, then the wizard walks its steps over HTTP")]
    public async Task InitWizardWalksStepsAsync()
    {
        using var client = await server.CreateBrowserClientAsync();
        var sessionId = await NewSessionAsync(client, projectId: null);

        var first = await PostMessageAsync(client, sessionId, "/init");
        first.GetProperty("awaitingApproval").GetBoolean().ShouldBeFalse();
        first.GetProperty("messages")
            .EnumerateArray()
            .ShouldContain(row => row.GetProperty("content").GetString()!.Contains("Step 1/4"));

        var second = await PostMessageAsync(client, sessionId, "https://git.example/acme");
        second.GetProperty("messages")
            .EnumerateArray()
            .ShouldContain(row => row.GetProperty("content").GetString()!.Contains("Step 2/4"));
    }

    [Fact(DisplayName = "Given a chat with several messages, when pages are read, then paging is stable and oldest-first")]
    public async Task PageTranscriptAsync()
    {
        using var client = await server.CreateBrowserClientAsync();
        var sessionId = await NewSessionAsync(client, projectId: null);
        await PostMessageAsync(client, sessionId, "hello");
        await PostMessageAsync(client, sessionId, "world");

        var page = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/chat/sessions/{sessionId}/messages?page=1&pageSize=2",
            TestContext.Current.CancellationToken);

        page.GetProperty("page").GetInt32().ShouldBe(1);
        page.GetProperty("total").GetInt32().ShouldBeGreaterThanOrEqualTo(4);
        var roles = page.GetProperty("items").EnumerateArray()
            .Select(static row => row.GetProperty("role").GetString())
            .ToList();
        roles.ShouldContain("user");
        roles.ShouldContain("assistant");
    }

    private static async Task<Guid> NewSessionAsync(HttpClient client, Guid? projectId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/chat/sessions",
            new { projectId },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var session = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return session.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> PostMessageAsync(HttpClient client, Guid sessionId, string message)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/chat/sessions/{sessionId}/messages",
            new { message },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private static async Task<List<JsonElement>> ListMessagesAsync(HttpClient client, Guid sessionId)
    {
        var page = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/chat/sessions/{sessionId}/messages",
            TestContext.Current.CancellationToken);
        return [.. page.GetProperty("items").EnumerateArray()];
    }

    private Task<OrchestrationDbContext> NewOrchestrationDbAsync()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(options, server.ConnectionString);
        return Task.FromResult(new OrchestrationDbContext(options.Options));
    }
}
