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
/// Inbound pull-request admission (issue #27): a signed
/// <c>pull_request.opened</c> webhook creates a run on the real host
/// composition. The run's work item claims on the <c>pr-review</c>
/// profile (not <c>general</c>), because the profile router picks
/// PR-kind tickets by default.
/// </summary>
[Collection(nameof(IntakeHostCollection))]
public sealed class PullRequestWebhookShould(HostIntakeServer server)
{
    private readonly HostIntakeServer server = server;

    [Fact(DisplayName = "Given a watch rule and a pull_request.opened webhook, when admitted, then a run is created on the pr-review profile")]
    public async Task AdmitPullRequestOnPrReviewProfileAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var browser = await server.CreateBrowserClientAsync();
        using var anonymous = server.CreateAnonymousClient();

        var projectId = ProjectId.New().Value;
        var webhookPath = await CreateConnectionAsync(browser, projectId, "pr hook");
        await CreateWatchRuleAsync(browser, projectId, /*lang=json,strict*/ """{"labelsAny": ["needs-review"]}""");

        var payload = await HostIntakeFiles.ReadFixtureAsync("github-pull-request-opened.json");
        var deliveryId = "pr-" + Guid.NewGuid().ToString("N");

        var response = await PostWebhookAsync(anonymous, webhookPath, payload, deliveryId);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await WebhooksShould.OutcomeOfAsync(response)).ShouldBe("admitted");

        await using var db = server.CreateOrchestrationDb();
        var run = await db.Runs.AsNoTracking()
            .SingleAsync(run => run.ProjectId == new ProjectId(projectId), cancellationToken);
        var workItem = await db.WorkItems.AsNoTracking()
            .SingleAsync(item => item.RunId == run.Id, cancellationToken);

        workItem.ProfileKey.ShouldBe("pr-review");
    }

    private static async Task<string> CreateConnectionAsync(HttpClient browser, Guid projectId, string name)
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
            cancellationToken: TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var view = await HostIntakeFiles.ReadJsonAsync(response);
        return view.GetProperty("webhookPath").GetString().ShouldNotBeNull();
    }

    private static async Task CreateWatchRuleAsync(HttpClient browser, Guid projectId, string filterJson)
    {
        var response = await browser.PostAsJsonAsync(
            "/api/v1/admission-rules",
            new { projectId, mode = "watch", filterJson },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<HttpResponseMessage> PostWebhookAsync(
        HttpClient client,
        string webhookPath,
        byte[] payload,
        string deliveryId)
    {
        var content = new ByteArrayContent(payload);
        content.Headers.Add("Content-Type", "application/json");
        content.Headers.Add("X-GitHub-Delivery", deliveryId);
        content.Headers.Add("X-GitHub-Event", "pull_request");
        content.Headers.Add(
            "X-Hub-Signature-256",
            "sha256=" + Convert.ToHexString(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(HostIntakeServer.HookSecret),
                payload)).ToLowerInvariant());

        return await client.PostAsync(webhookPath, content, TestContext.Current.CancellationToken);
    }
}
