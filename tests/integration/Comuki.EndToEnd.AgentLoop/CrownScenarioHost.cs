using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host;
using Comuki.Host.Testing;
using Comuki.Host.Testing.Fixtures;
using Comuki.Shared.Contracts.Queue;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Comuki.EndToEnd.AgentLoop;

/// <summary>
/// Boots the real host composition (<see cref="HostComposer"/>) on a
/// random loopback port against one shared, migrated Postgres owned by
/// this type's own <see cref="PostgresCollectionFixture"/> — modelled
/// exactly on <c>tests/integration/Comuki.Host.Integration.Intake/HostIntakeServer</c>.
/// </summary>
/// <remarks>
/// <see cref="PostgresCollectionFixture"/> is owned by containment (a
/// plain field whose lifecycle this type drives itself), not declared as
/// a second <c>ICollectionFixture&lt;T&gt;</c> on the same collection —
/// xUnit v3 does not thread one collection fixture into another's
/// constructor (verified empirically by the existing
/// <see cref="AgentLoopHost"/>'s remarks).
///
/// <para><b>Substitution note (documented here as the brief requires).</b>
/// The T2a agent-loop scenario suite (<see cref="AgentLoopHost"/> +
/// <c>Comuki.AgentTest.Runner</c>'s container-based
/// <c>DockerComputeProvider</c> + <c>ContainerHostAddressResolver</c>)
/// is blocked in this Podman/WSL2 sandbox by issues #152 (gRPC h2c) and
/// #153 (no container-reachable host address) — see
/// <c>.agents/rules/process/local-test-runtime.md</c>. This fixture
/// intentionally does <b>not</b> boot a Docker/Podman worker container
/// and does <b>not</b> call <c>AddWorkerRuntime</c>/<c>MapWorkerRuntime</c>;
/// "workers" in this suite are in-process <see cref="IWorkItemQueue"/>
/// calls resolved from the real host composition's
/// <see cref="IServiceProvider"/>. The same in-process substitution is
/// already the proven pattern in
/// <c>RunDecisionsEndpointShould.CancelFencesLiveWorkItemAsync</c> and
/// the entire <c>Comuki.Engine.Orchestration.Integration.Queue</c>
/// suite, all of which run against the same shared Postgres fixture.
/// </para>
/// </remarks>
public sealed class CrownScenarioHost : IAsyncLifetime
{
    private const string HookSecretEnv = "COMUKI_CROWN_HOOK_SECRET";
    private const string HookSecret = "crown-test-hook-secret";

    private readonly PostgresCollectionFixture postgres = new();

    private WebApplication application = null!;
    private Uri baseAddress = null!;

    /// <summary>The database connection string (direct context access for asserts).</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>The composed host's root service provider — for tests that resolve a scoped service
    /// directly (e.g. <see cref="IWorkItemQueue"/>) rather than going through HTTP.</summary>
    public IServiceProvider Services => application.Services;

    /// <summary>The shared <see cref="WorkItemLabels"/> value stamped on every WorkItem seeded for or by this suite
    /// (the <c>Intake:Worker:*</c> config on this host + the explicit seeds in <see cref="CrownScenarioShould"/>),
    /// so a single <see cref="IWorkItemQueue.ClaimAsync"/> call covers them all. Mirror of the host's
    /// intake-worker defaults — change <c>Intake:Worker:*</c> above and update this constant with it.</summary>
    public static WorkItemLabels EntryLabels => new("ghcr.io/comuki/worker:crown-test", "crown-test", "implement");

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await postgres.InitializeAsync();
        ConnectionString = postgres.ConnectionString;

        Environment.SetEnvironmentVariable(HookSecretEnv, HookSecret, EnvironmentVariableTarget.Process);

        var builder = TestHostBuilder.Create(ConnectionString);
        TestBootstrapAdmin.Configure(builder.Configuration);
        TestArtifactsSecrets.ApplyPlaceholder(builder.Configuration);
        // Lift rate-limit partitions for the integration run — this suite posts
        // a webhook + a /cancel in tight succession and the login helper's retry
        // loop must not spend its budget on 429s.
        builder.Configuration["Host:RateLimit:LoginPermitsPerMinute"] = "10000";
        builder.Configuration["Host:RateLimit:ApiPermitsPerMinute"] = "100000";
        builder.Configuration["Host:RateLimit:RunDecisionPermitsPerMinute"] = "10000";
        builder.Configuration["Host:RateLimit:OidcStartPermitsPerMinute"] = "10000";
        builder.Configuration["Intake:BridgeInterval"] = "00:00:01";
        builder.Configuration["Intake:SyncBackoff"] = "00:00:01";
        // Claim labels every WorkItem seeded by this suite uses — fixed at host
        // boot (Intake:Worker:* binds once); the in-process worker caller in
        // CrownScenarioShould only ever asks for these exact strings, so one
        // ClaimAsync covers the intake-created item AND the directly-seeded
        // dependents alike.
        builder.Configuration["Intake:Worker:Image"] = "ghcr.io/comuki/worker:crown-test";
        builder.Configuration["Intake:Worker:ProfilesRef"] = "crown-test";
        builder.Configuration["Intake:Worker:IssueDefaultProfileKey"] = "implement";

        application = await HostComposer.ComposeAsync(builder, HostDatabase.Explicit(ConnectionString));
        baseAddress = await TestHostBuilder.StartAsync(application, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (application is not null)
        {
            await application.DisposeAsync();
        }

        await postgres.DisposeAsync();
    }

    /// <summary>Cookie-carrying browser client logged in as the bootstrap admin.</summary>
    public Task<HttpClient> CreateBrowserClientAsync()
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = true, CheckCertificateRevocationList = true })
        {
            BaseAddress = baseAddress,
        };

        return client.LoginAsBootstrapAdminAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Cookie-less anonymous client.</summary>
    public HttpClient CreateAnonymousClient()
    {
        return new HttpClient { BaseAddress = baseAddress };
    }

    /// <summary>One fresh orchestration context with system-scope unrestricted row visibility, for direct asserts.</summary>
    public OrchestrationDbContext CreateOrchestrationDb()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(options, ConnectionString);
        return new OrchestrationDbContext(options.Options);
    }

    /// <summary>POST a signed GitHub-issues payload through the real webhook endpoint.</summary>
    /// <param name="client">Anonymous client — the signature is the auth.</param>
    /// <param name="webhookPath">Path returned by <see cref="ProvisionWebhookAsync"/>.</param>
    /// <param name="payload">Raw JSON body — exactly the same bytes on a replay.</param>
    /// <param name="deliveryId"><c>X-GitHub-Delivery</c> id; the same id on a replay makes the second POST a no-op.</param>
    public static async Task<HttpResponseMessage> PostWebhookAsync(
        HttpClient client,
        string webhookPath,
        byte[] payload,
        string deliveryId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var signature = "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(HookSecret), payload)).ToLowerInvariant();

        using var content = new ByteArrayContent(payload);
        content.Headers.Add("Content-Type", "application/json");
        content.Headers.Add("X-GitHub-Delivery", deliveryId);
        content.Headers.Add("X-GitHub-Event", "issues");
        content.Headers.Add("X-Hub-Signature-256", signature);

        return await client.PostAsync(webhookPath, content, cancellationToken);
    }

    /// <summary>One-time source connection + watch admission rule; returns the webhook path the source POST should land on.</summary>
    /// <param name="browser">Logged-in browser client.</param>
    /// <param name="projectId">Fresh project id this webhook belongs to.</param>
    /// <param name="admissionLabel">Label the watch rule matches on.</param>
    public static async Task<string> ProvisionWebhookAsync(HttpClient browser, Guid projectId, string admissionLabel)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var connectionResponse = await browser.PostAsJsonAsync(
            "/api/v1/sources",
            new
            {
                projectId,
                provider = "github",
                name = "crown scenario hook",
                settingsJson = /*lang=json,strict*/ """{"owner": "comuki", "repo": "crown-scenario"}""",
                secretEnvRef = HookSecretEnv,
            },
            cancellationToken);
        connectionResponse.EnsureSuccessStatusCode();
        using var connectionDocument = JsonDocument.Parse(await connectionResponse.Content.ReadAsStringAsync(cancellationToken));

        var ruleResponse = await browser.PostAsJsonAsync(
            "/api/v1/admission-rules",
            new
            {
                projectId,
                mode = "watch",
                filterJson = /*lang=json,strict*/ $$"""{"labelsAny": ["{{admissionLabel}}"]}""",
            },
            cancellationToken);
        ruleResponse.EnsureSuccessStatusCode();

        return connectionDocument.RootElement.GetProperty("webhookPath").GetString()
            ?? throw new InvalidOperationException("source response did not carry a webhookPath");
    }

    /// <summary>Build the minimal GitHub "issues.opened" payload the host's GitHubPayloadMapper accepts
    /// (action "opened", issue.number/title/body/html_url/state/user/labels, repository.full_name).</summary>
    public static byte[] BuildGithubIssuePayload(
        string title,
        string body,
        IReadOnlyList<string> labels,
        int issueNumber,
        string repoFullName)
    {
        var payload = new
        {
            action = "opened",
            issue = new
            {
                number = issueNumber,
                title,
                body,
                html_url = $"https://github.com/{repoFullName}/issues/{issueNumber}",
                state = "open",
                user = new { login = "crown-scenario-fixture" },
                labels = labels.Select(static label => new { name = label }).ToArray(),
            },
            repository = new { full_name = repoFullName },
        };
        return JsonSerializer.SerializeToUtf8Bytes(payload, JsonSerializerOptions.Web);
    }
}
