using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Comuki.AgentTest.Runner.Compute;
using Comuki.AgentTest.Runner.Execution;
using Comuki.Engine.Compute.Providers;
using Comuki.Engine.Compute.Security;
using Comuki.Engine.Orchestration.Application;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host;
using Comuki.Host.Testing;
using Comuki.Host.Testing.Fixtures;
using Comuki.Host.Workers;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Docker.DotNet;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.EndToEnd.AgentLoop;

/// <summary>
/// Boots the real host composition (<see cref="HostComposer"/> +
/// <c>AddWorkerRuntime</c>/<c>MapWorkerRuntime</c> — the same sequence
/// <c>platform/src/host/Comuki.Host/Program.cs</c> uses, see this repo's
/// <c>WorkerUploadArtifactShould</c> for the proven precedent) on the
/// shared WS2 <see cref="PostgresCollectionFixture"/>, plus a real
/// <see cref="DockerComputeProvider"/> pointed at Podman and the WS6 test
/// worker image, for the T2a agent-loop suite.
/// </summary>
/// <remarks>
/// <see cref="PostgresCollectionFixture"/> is owned by containment (a
/// plain field, its lifecycle driven from this type's own), not declared
/// as a second <c>ICollectionFixture&lt;T&gt;</c> on the same collection —
/// verified empirically in this repo (WS2's <c>AuthIntegrationCollection</c>
/// remarks) that xUnit v3 does not thread one collection fixture into
/// another's constructor.
/// </remarks>
public sealed class AgentLoopHost : IAsyncLifetime
{
    private const string HookSecretEnv = "COMUKI_AGENT_LOOP_HOOK_SECRET";
    private const string HookSecret = "agent-loop-test-hook-secret";
    private const string AdmissionLabel = "comuki";

    private readonly PostgresCollectionFixture postgres = new();

    private WebApplication application = null!;
    private Uri baseAddress = null!;
    private DockerClient dockerClient = null!;
    private string webhookPath = string.Empty;
    private int hostPort;
    private string containerReachableHost = string.Empty;

    /// <summary>The real, unmodified Docker compute provider this suite exercises — see <see cref="AgentLoopHarness"/>.</summary>
    public DockerComputeProvider ComputeProvider { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await postgres.InitializeAsync();

        var repositoryRoot = ResolveRepositoryRoot();
        await WorkerTestImage.BuildAsync(repositoryRoot, cancellationToken);

        dockerClient = DockerComputeProviderFactory.CreateClient();
        ComputeProvider = DockerComputeProviderFactory.Create(dockerClient, runAsUser: "1000");

        hostPort = FreeTcpPort.Next();

        var builder = TestHostBuilder.Create(postgres.ConnectionString);
        // Production topology on one loopback (Program.cs): REST HTTP/1.1
        // and the worker gRPC bidi stream (h2c) share one Kestrel listener.
        // Bound to 0.0.0.0 (not TestHostBuilder's default loopback-only) so
        // a real container reaches this process — see
        // Comuki.AgentTest.Runner.Compute.ContainerHostAddressResolver's
        // remarks for why loopback alone does not work here.
        builder.WebHost.ConfigureKestrel(static server =>
            server.ConfigureEndpointDefaults(static listen => listen.Protocols = HttpProtocols.Http1AndHttp2));
        builder.WebHost.UseUrls($"http://0.0.0.0:{hostPort}");
        builder.Configuration["auth:publicHost:publicUrl"] = $"http://127.0.0.1:{hostPort}";
        TestBootstrapAdmin.Configure(builder.Configuration);
        TestArtifactsSecrets.ApplyPlaceholder(builder.Configuration);
        builder.Configuration["Host:RateLimit:LoginPermitsPerMinute"] = "10000";

        // Claim labels every scenario in this suite's fixture corpus uses —
        // fixed at host boot (Intake:Worker:* binds once); a scenario's own
        // worker.image/profileKey/profilesRef only drive what
        // AgentLoopHarness.StartWorkerAsync stamps on the real container,
        // and must equal these for a real claim to match (WorkItemQueueSql's
        // claim predicate is exact-string, not fuzzy).
        builder.Configuration["Intake:Worker:Image"] = WorkerTestImage.Tag;
        builder.Configuration["Intake:Worker:ProfilesRef"] = "test";
        builder.Configuration["Intake:Worker:IssueDefaultProfileKey"] = "implement";
        builder.Configuration["Intake:BridgeInterval"] = "00:00:01";

        Environment.SetEnvironmentVariable(HookSecretEnv, HookSecret, EnvironmentVariableTarget.Process);

        builder.Services
            .AddOrchestrationApplication()
            .AddWorkerRuntime(builder.Configuration);

        application = await HostComposer.ComposeAsync(builder, HostDatabase.Explicit(postgres.ConnectionString));
        application.MapWorkerRuntime();
        // Kestrel is bound to 0.0.0.0 (see above) so a real container can
        // reach it — TestHostBuilder.StartAsync echoes that literal bind
        // address back (IServerAddressesFeature reports "http://0.0.0.0:{port}"
        // for a wildcard bind, not a connectable address), so it is not used
        // here. This test process's own HttpClient calls (webhook
        // provisioning/posting) always go over loopback; the container
        // reaches this host via containerReachableHost instead (resolved
        // below).
        await TestHostBuilder.StartAsync(application, cancellationToken);
        baseAddress = new Uri($"http://127.0.0.1:{hostPort}/");

        webhookPath = await ProvisionWebhookAsync(cancellationToken);

        containerReachableHost = await ContainerHostAddressResolver.ResolveAsync(
            dockerClient.Containers,
            WorkerTestImage.Tag,
            networkMode: DockerComputeProviderFactory.ResolveNetworkMode(),
            hostPort: hostPort,
            healthPath: ApiRoutes.Health,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Tears down whatever <see cref="InitializeAsync"/> managed to create —
    /// deliberately null-tolerant: a partial-initialization failure (e.g.
    /// the image build failing before <see cref="application"/>/
    /// <see cref="dockerClient"/> are assigned) must still let the real
    /// original exception surface, not be masked by a
    /// <see cref="NullReferenceException"/> from cleanup.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (application is not null)
        {
            await application.DisposeAsync();
        }

        dockerClient?.Dispose();
        await postgres.DisposeAsync();
    }

    /// <summary>Resolves the token/gRPC-stream contract a real worker container needs — see <see cref="AgentLoopHarness"/>.</summary>
    public WorkerTokenIssuer ResolveTokenIssuer()
    {
        return application.Services.GetRequiredService<WorkerTokenIssuer>();
    }

    /// <summary>The gRPC/REST base URL the container's Translator reaches this host on — a real address (see <see cref="containerReachableHost"/>), not loopback.</summary>
    public Uri ContainerReachableBaseUri()
    {
        return new Uri($"http://{containerReachableHost}:{hostPort}/");
    }

    /// <summary>Posts a GitHub issue webhook through the real webhook endpoint and returns the run/work-item <c>IntakeRunLauncher</c> created.</summary>
    /// <param name="title"></param>
    /// <param name="body"></param>
    /// <param name="labels"></param>
    /// <param name="issueNumber">A unique issue number so two scenarios in the same suite never collide on intake's duplicate-active-ticket check.</param>
    /// <param name="cancellationToken"></param>
    public async Task<SeededWorkItem> SeedTicketAsync(
        string title,
        string body,
        IReadOnlyList<string> labels,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        var payload = BuildGithubIssuePayload(title, body, labels, issueNumber);
        var deliveryId = "agent-loop-" + Guid.NewGuid().ToString("N");
        var signature = "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(HookSecret), payload)).ToLowerInvariant();

        using var anonymous = new HttpClient { BaseAddress = baseAddress };
        using var content = new ByteArrayContent(payload);
        content.Headers.Add("Content-Type", "application/json");
        content.Headers.Add("X-GitHub-Delivery", deliveryId);
        content.Headers.Add("X-GitHub-Event", "issues");
        content.Headers.Add("X-Hub-Signature-256", signature);

        var response = await anonymous.PostAsync(webhookPath, content, cancellationToken);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        document.RootElement.GetProperty("outcome").GetString().ShouldBe("admitted");

        // IntakeItemBrief.ToJson (Comuki.Host.Intake.IntakeRunLauncher) embeds
        // the ticket's externalId as "{repository.full_name}#{issueNumber}"
        // (GitHubPayloadMapper.MapIssue) — matches the payload built above.
        var externalId = $"comuki/agent-loop-fixture#{issueNumber}";
        await using var scope = application.Services.CreateAsyncScope();
        using var systemScope = scope.ServiceProvider.GetRequiredService<ISubjectScopeAccessor>().AsSystem("agent-loop-fixture");
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        // Brief is a jsonb column — Postgres has no LIKE (~~) operator over
        // jsonb, so a translated .Contains() on it throws 42883. The recent
        // slice is small (one webhook per scenario in this suite); filter
        // client-side instead of fighting the SQL translation.
        var recent = await db.WorkItems
            .AsNoTracking()
            .OrderByDescending(static candidate => candidate.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);
        var item = recent.First(candidate => candidate.Brief.Contains(externalId, StringComparison.Ordinal));
        return new SeededWorkItem(item.RunId.Value, item.Id);
    }

    /// <summary>Reads the run's timeline through the real <see cref="IRunJournal"/>, oldest first.</summary>
    public async Task<IReadOnlyList<RunEventEntry>> ReadTimelineAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var scope = application.Services.CreateAsyncScope();
        using var systemScope = scope.ServiceProvider.GetRequiredService<ISubjectScopeAccessor>().AsSystem("agent-loop-fixture");
        var journal = scope.ServiceProvider.GetRequiredService<IRunJournal>();
        return await journal.ReadTimelineAsync(new RunId(runId), page: 1, pageSize: 200, cancellationToken);
    }

    /// <summary>Reads the work item's current status string.</summary>
    public async Task<string> ReadWorkItemStatusAsync(Guid workItemId, CancellationToken cancellationToken)
    {
        await using var scope = application.Services.CreateAsyncScope();
        using var systemScope = scope.ServiceProvider.GetRequiredService<ISubjectScopeAccessor>().AsSystem("agent-loop-fixture");
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        var item = await db.WorkItems.AsNoTracking().SingleAsync(candidate => candidate.Id == workItemId, cancellationToken);
        return item.Status.ToString();
    }

    /// <summary>One-time source connection + watch admission rule every scenario's webhook lands through.</summary>
    private async Task<string> ProvisionWebhookAsync(CancellationToken cancellationToken)
    {
        using var browser = new HttpClient(new HttpClientHandler { UseCookies = true, CheckCertificateRevocationList = true })
        {
            BaseAddress = baseAddress,
        };
        await browser.LoginAsBootstrapAdminAsync(cancellationToken);

        var projectId = ProjectId.New().Value;
        var connectionResponse = await browser.PostAsJsonAsync(
            "/api/v1/sources",
            new
            {
                projectId,
                provider = "github",
                name = "agent-loop hook",
                settingsJson = /*lang=json,strict*/ """{"owner": "comuki", "repo": "agent-loop-fixture"}""",
                secretEnvRef = HookSecretEnv,
            },
            cancellationToken);
        connectionResponse.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created, await connectionResponse.Content.ReadAsStringAsync(cancellationToken));
        using var connectionDocument = JsonDocument.Parse(await connectionResponse.Content.ReadAsStringAsync(cancellationToken));

        var ruleResponse = await browser.PostAsJsonAsync(
            "/api/v1/admission-rules",
            new { projectId, mode = "watch", filterJson = /*lang=json,strict*/ $$"""{"labelsAny": ["{{AdmissionLabel}}"]}""" },
            cancellationToken);
        ruleResponse.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created, await ruleResponse.Content.ReadAsStringAsync(cancellationToken));

        return connectionDocument.RootElement.GetProperty("webhookPath").GetString().ShouldNotBeNull();
    }

    private static byte[] BuildGithubIssuePayload(string title, string body, IReadOnlyList<string> labels, int issueNumber)
    {
        var payload = new
        {
            action = "opened",
            issue = new
            {
                number = issueNumber,
                title,
                body,
                html_url = $"https://github.com/comuki/agent-loop-fixture/issues/{issueNumber}",
                state = "open",
                user = new { login = "agent-loop-fixture" },
                labels = labels.Select(static label => new { name = label }).ToArray(),
            },
            repository = new { full_name = "comuki/agent-loop-fixture" },
        };
        return JsonSerializer.SerializeToUtf8Bytes(payload, JsonSerializerOptions.Web);
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "comuki.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("could not locate the repo root (comuki.slnx) from " + AppContext.BaseDirectory);
    }
}
