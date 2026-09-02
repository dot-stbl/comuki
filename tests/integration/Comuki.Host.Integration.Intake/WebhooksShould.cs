using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Intake;

/// <summary>
/// The webhook flow end to end on the real composition: watch admission
/// creates a run, replays are no-ops, duplicate-active deliveries are
/// dropped, bad signatures are rejected, and the inbox claim runs once.
/// </summary>
[Collection(nameof(IntakeHostCollection))]
public sealed class WebhooksShould(HostIntakeServer server)
{
    private readonly HostIntakeServer server = server;

    [Fact(DisplayName = "Given a watch rule and connection, when a signed issue webhook arrives, then a run is created and the replay is a 200 no-op")]
    public async Task CreateRunOnWatchWebhookAsync()
    {
        _ = TestContext.Current.CancellationToken;
        using var browser = await server.CreateBrowserClientAsync();
        using var anonymous = server.CreateAnonymousClient();

        var projectId = ProjectId.New().Value;
        var webhookPath = await CreateConnectionAsync(browser, projectId, "watch hook");
        await CreateRuleAsync(browser, projectId, "watch", /*lang=json,strict*/ """{"labelsAny": ["comuki"]}""");

        var payload = await HostIntakeFiles.ReadFixtureAsync("github-issue-opened.json");
        var deliveryId = "delivery-" + Guid.NewGuid().ToString("N");

        // anonymous — the signature IS the auth
        var first = await PostWebhookAsync(anonymous, webhookPath, payload, deliveryId);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await OutcomeOfAsync(first)).ShouldBe("admitted");

        var runId = await SingleRunOfProjectAsync(projectId);
        runId.ShouldNotBe(Guid.Empty);

        // replay: the same delivery id → 200 replay, no second run
        var replay = await PostWebhookAsync(anonymous, webhookPath, payload, deliveryId);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await OutcomeOfAsync(replay)).ShouldBe("replay");
        (await SingleRunOfProjectAsync(projectId)).ShouldBe(runId);
    }

    [Fact(DisplayName = "Given an active ticket, when another delivery of the same issue arrives, then it is a duplicate and no second run exists")]
    public async Task DropDuplicateActiveDeliveryAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var browser = await server.CreateBrowserClientAsync();
        using var anonymous = server.CreateAnonymousClient();

        var projectId = ProjectId.New().Value;
        var webhookPath = await CreateConnectionAsync(browser, projectId, "dup hook");
        await CreateRuleAsync(browser, projectId, "watch", /*lang=json,strict*/ """{"labelsAny": ["comuki"]}""");

        var opened = await PostWebhookAsync(
            anonymous, webhookPath, await HostIntakeFiles.ReadFixtureAsync("github-issue-opened.json"), "dup-open-" + Guid.NewGuid().ToString("N"));
        (await OutcomeOfAsync(opened)).ShouldBe("admitted");

        // same issue (#481, dot-stbl/comuki), different letter (labeled event)
        var labeled = await PostWebhookAsync(
            anonymous, webhookPath, await HostIntakeFiles.ReadFixtureAsync("github-issue-labeled.json"), "dup-label-" + Guid.NewGuid().ToString("N"));
        labeled.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await OutcomeOfAsync(labeled)).ShouldBe("duplicate");

        await using var db = server.CreateOrchestrationDb();
        (await db.Runs.CountAsync(run => run.ProjectId == new ProjectId(projectId), cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName = "Given a webhook without a valid signature, when posted, then it is rejected 401 before any processing")]
    public async Task RejectBadSignatureAsync()
    {
        using var browser = await server.CreateBrowserClientAsync();
        using var anonymous = server.CreateAnonymousClient();

        var projectId = ProjectId.New().Value;
        var webhookPath = await CreateConnectionAsync(browser, projectId, "bad sig hook");
        await CreateRuleAsync(browser, projectId, "watch", /*lang=json,strict*/ """{"labelsAny": ["comuki"]}""");

        var payload = await HostIntakeFiles.ReadFixtureAsync("github-issue-opened.json");
        var response = await PostWebhookAsync(anonymous, webhookPath, payload, "badsig-" + Guid.NewGuid().ToString("N"), signature: "sha256=" + new string('0', 64));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await using var db = server.CreateOrchestrationDb();
        (await db.Runs.CountAsync(run => run.ProjectId == new ProjectId(projectId), TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact(DisplayName = "Given an inbox rule, when a matching webhook arrives, then the ticket parks pending and one claim creates the run once")]
    public async Task InboxClaimCreatesRunOnceAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var browser = await server.CreateBrowserClientAsync();
        using var anonymous = server.CreateAnonymousClient();

        var projectId = ProjectId.New().Value;
        var webhookPath = await CreateConnectionAsync(browser, projectId, "inbox hook");
        await CreateRuleAsync(browser, projectId, "inbox", /*lang=json,strict*/ """{"labelsAny": ["comuki"]}""");

        var delivery = await PostWebhookAsync(
            anonymous, webhookPath, await HostIntakeFiles.ReadFixtureAsync("github-issue-opened.json"), "inbox-" + Guid.NewGuid().ToString("N"));
        (await OutcomeOfAsync(delivery)).ShouldBe("pending");

        // the ticket sits in the inbox
        var inbox = await browser.GetAsync("/api/v1/inbox?projectId=" + projectId, cancellationToken);
        inbox.StatusCode.ShouldBe(HttpStatusCode.OK);
        var pending = await HostIntakeFiles.ReadJsonAsync(inbox);
        var ticketId = pending[0].GetProperty("id").GetString();
        ticketId.ShouldNotBeNull();

        // claim → run; repeat claim → 409
        var claim = await browser.PostAsJsonAsync(
            "/api/v1/inbox/claim",
            new { ticketId = Guid.Parse(ticketId) },
            cancellationToken);
        claim.StatusCode.ShouldBe(HttpStatusCode.OK);
        var claimedView = await HostIntakeFiles.ReadJsonAsync(claim);
        var runId = claimedView.GetProperty("runId").GetString();
        runId.ShouldNotBeNull();

        var claimAgain = await browser.PostAsJsonAsync(
            "/api/v1/inbox/claim",
            new { ticketId = Guid.Parse(ticketId) },
            cancellationToken);
        claimAgain.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = server.CreateOrchestrationDb();
        (await db.Runs.CountAsync(run => run.ProjectId == new ProjectId(projectId), cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName = "Given a non-matching admission filter, when a webhook arrives, then the ticket is filtered and never runs")]
    public async Task FilterOutNonMatchingTicketAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var browser = await server.CreateBrowserClientAsync();
        using var anonymous = server.CreateAnonymousClient();

        var projectId = ProjectId.New().Value;
        var webhookPath = await CreateConnectionAsync(browser, projectId, "filter hook");
        await CreateRuleAsync(browser, projectId, "watch", /*lang=json,strict*/ """{"labelsAny": ["wont-match"]}""");

        var delivery = await PostWebhookAsync(
            anonymous, webhookPath, await HostIntakeFiles.ReadFixtureAsync("github-issue-opened.json"), "filtered-" + Guid.NewGuid().ToString("N"));
        (await OutcomeOfAsync(delivery)).ShouldBe("filtered");

        await using var db = server.CreateOrchestrationDb();
        (await db.Runs.CountAsync(run => run.ProjectId == new ProjectId(projectId), cancellationToken)).ShouldBe(0);
    }

    internal static async Task<string> OutcomeOfAsync(HttpResponseMessage response)
    {
        var json = await HostIntakeFiles.ReadJsonAsync(response);
        return json.GetProperty("outcome").GetString().ShouldNotBeNull();
    }

    private async Task<Guid> SingleRunOfProjectAsync(Guid projectId)
    {
        await using var db = server.CreateOrchestrationDb();
        var run = await db.Runs.AsNoTracking().SingleAsync(
            run => run.ProjectId == new ProjectId(projectId),
            TestContext.Current.CancellationToken);
        return run.Id.Value;
    }

    internal static async Task<string> CreateConnectionAsync(HttpClient browser, Guid projectId, string name)
    {
        var response = await browser.PostAsJsonAsync(
            "/api/v1/sources",
            new
            {
                projectId,
                provider = "github",
                name,
                settingsJson = /*lang=json,strict*/ """{"owner": "dot-stbl", "repo": "comuki"}""",
                secretEnvRef = HostIntakeServer.HookSecretEnv,
            },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var view = await HostIntakeFiles.ReadJsonAsync(response);
        return view.GetProperty("webhookPath").GetString().ShouldNotBeNull();
    }

    internal static async Task CreateRuleAsync(HttpClient browser, Guid projectId, string mode, string filterJson)
    {
        var response = await browser.PostAsJsonAsync(
            "/api/v1/admission-rules",
            new { projectId, mode, filterJson },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    internal static async Task<HttpResponseMessage> PostWebhookAsync(
        HttpClient client,
        string webhookPath,
        byte[] payload,
        string deliveryId,
        string? signature = null)
    {
        var content = new ByteArrayContent(payload);
        content.Headers.Add("Content-Type", "application/json");
        content.Headers.Add("X-GitHub-Delivery", deliveryId);
        content.Headers.Add("X-GitHub-Event", "issues");
        content.Headers.Add(
            "X-Hub-Signature-256",
            signature ?? "sha256=" + Convert.ToHexString(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(HostIntakeServer.HookSecret),
                payload)).ToLowerInvariant());

        return await client.PostAsync(webhookPath, content, TestContext.Current.CancellationToken);
    }
}
