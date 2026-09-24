using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Comuki.AgentTest.Runner.Execution;
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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.EndToEnd.AgentLoop.RealPi;

/// <summary>
/// T2b's host (design.md D3 / WS7): the same real webhook -&gt; run/work-item
/// creation -&gt; queue path <see cref="AgentLoopHost"/> proves for T2a, minus
/// every Docker/Podman dependency — no container is provisioned anywhere
/// in this class. A real pi process (<see cref="RealPiInstallation"/>) is
/// spawned in-process by <see cref="RealPiFakeModelHarness"/> against this
/// host's real REST claim/heartbeat/complete surface and real gRPC bidi
/// stream, both on loopback only (nothing outside this test process ever
/// needs to reach them, unlike T2a's container-reachable bind).
/// </summary>
/// <remarks>
/// Deliberately not built from <see cref="AgentLoopHost"/> by inheritance
/// or composition — that class's <c>InitializeAsync</c> interleaves the
/// Docker-specific steps (image build, <c>DockerComputeProviderFactory</c>,
/// <c>ContainerHostAddressResolver</c>) with the host-boot steps this class
/// needs too tightly to extract a shared base without a larger refactor of
/// already-passing WS6 code, which is out of this workstream's file scope.
/// The boot sequence below (Postgres fixture, <c>TestHostBuilder</c>,
/// <c>HostComposer.ComposeAsync</c>, <c>MapWorkerRuntime</c>, webhook
/// provisioning) is otherwise the same recipe, verified against the same
/// proven precedent.
/// </remarks>
public sealed class RealPiFakeModelHost : IAsyncLifetime
{
    private const string HookSecretEnv = "COMUKI_REAL_PI_HOOK_SECRET";
    private const string HookSecret = "real-pi-fake-model-test-hook-secret";
    private const string AdmissionLabel = "comuki";

    private readonly PostgresCollectionFixture postgres = new();

    // boundary: assigned in InitializeAsync (IAsyncLifetime) before any caller can observe it.
    private WebApplication application = null!;
    private string webhookPath = string.Empty;

    /// <summary>Loopback REST base address (claim/heartbeat/complete surface) — an HTTP/1-only listener, separate from <see cref="GrpcAddress"/>.</summary>
    // boundary: assigned in InitializeAsync (IAsyncLifetime) before any caller can observe it.
    public Uri BaseAddress { get; private set; } = null!;

    /// <summary>
    /// Loopback gRPC address (the worker bidi stream) — a separate,
    /// cleartext HTTP/2-only listener, not shared with <see cref="BaseAddress"/>.
    /// <c>Comuki.Host.Translator.Integration.PiCli.TestWorkerHost</c>'s own
    /// remarks explain why one mixed <c>Http1AndHttp2</c> listener is
    /// the wrong choice for a real gRPC client ("Kestrel answers the h2
    /// preface with HTTP_1_1_REQUIRED on an HTTP/1-only endpoint, and mixed
    /// Http1AndHttp2 still tripped the gRPC client") — confirmed here too:
    /// this host originally shared one mixed listener (matching
    /// <see cref="AgentLoopHost"/>'s container-reachable topology, which has
    /// no choice — a container needs one published address) and every
    /// <c>worker.reported</c> gRPC-streamed journal entry silently never
    /// landed (only the two REST-driven <c>work_item.status_changed</c>
    /// entries did) — the exact symptom issue #152 names, reproduced
    /// in-process/loopback, with no container or cross-VM latency involved
    /// at all. Splitting the listener (this two-port topology) fixed it.
    /// Narrows #152's hypothesis: the mixed-listener gRPC/REST topology
    /// itself is implicated, not (only) container/cross-VM timing — worth
    /// re-checking against #152 directly.
    /// </summary>
    // boundary: assigned in InitializeAsync (IAsyncLifetime) before any caller can observe it.
    public Uri GrpcAddress { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await postgres.InitializeAsync();

        var restPort = FreeTcpPort.Next();
        var grpcPort = FreeTcpPort.Next();

        var builder = TestHostBuilder.Create(postgres.ConnectionString);
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Listen(System.Net.IPAddress.Loopback, restPort, static listen => listen.Protocols = HttpProtocols.Http1);
            server.Listen(System.Net.IPAddress.Loopback, grpcPort, static listen => listen.Protocols = HttpProtocols.Http2);
        });
        builder.Configuration["auth:publicHost:publicUrl"] = $"http://127.0.0.1:{restPort}";
        TestBootstrapAdmin.Configure(builder.Configuration);
        TestArtifactsSecrets.ApplyPlaceholder(builder.Configuration);
        builder.Configuration["Host:RateLimit:LoginPermitsPerMinute"] = "10000";

        // Claim labels the three scenarios this host serves
        // (add-null-check-real-pi.scenario.yaml — WS7 fake — and
        // add-null-check-replay.scenario.yaml — WS8 replay, plus the
        // CassetteGeneratorShould's generated-scenario twin) use — fixed at
        // host boot, same reasoning as AgentLoopHost's own copy of this
        // comment. A single worker image label across the three is the
        // shared-collection trade-off documented on
        // RealPiHarnessBase.WorkerImageLabel — the queue claim SQL's
        // `image = @image` filter is an exact match, but the
        // collection's DisableParallelization means each scenario's
        // translator loop runs alone against the queue, so the only
        // Queued item at run time is the one the in-flight fact just
        // seeded.
        builder.Configuration["Intake:Worker:Image"] = RealPiFakeModelHarness.WorkerImageLabel;
        builder.Configuration["Intake:Worker:ProfilesRef"] = "test";
        builder.Configuration["Intake:Worker:IssueDefaultProfileKey"] = "implement";
        builder.Configuration["Intake:BridgeInterval"] = "00:00:01";

        Environment.SetEnvironmentVariable(HookSecretEnv, HookSecret, EnvironmentVariableTarget.Process);

        // No .AddProxyApplication(...) — AgentLoopHost's own T2a host omits
        // it too and claims succeed regardless (proven precedent): the
        // worker REST claim handler tolerates no virtual-key store being
        // registered when Proxy is simply never configured. Skipping it
        // here is deliberate, not an oversight — virtual-key minting is
        // already covered by Comuki.Host.Translator.Integration.PiCli's
        // TranslatorE2EShould; this host isolates exactly the thing WS7/
        // issue #150 needs proven (the PI_CODING_AGENT_DIR/models.json
        // redirect), undiluted by an orthogonal concern.
        builder.Services
            .AddOrchestrationApplication()
            .AddWorkerRuntime(builder.Configuration);

        application = await HostComposer.ComposeAsync(builder, HostDatabase.Explicit(postgres.ConnectionString));
        application.MapWorkerRuntime();
        // Not TestHostBuilder.StartAsync: that helper's IServerAddressesFeature.Addresses.Single()
        // assumes one bound address — this host deliberately binds two
        // (see GrpcAddress's remarks), so it starts the host and resolves
        // both addresses from the known ports directly instead.
        await application.StartAsync(cancellationToken);
        BaseAddress = new Uri($"http://127.0.0.1:{restPort}/");
        GrpcAddress = new Uri($"http://127.0.0.1:{grpcPort}/");

        webhookPath = await ProvisionWebhookAsync(cancellationToken);
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

    /// <summary>Resolves the token issuer <see cref="RealPiFakeModelHarness"/> mints a per-execution worker token from.</summary>
    public WorkerTokenIssuer ResolveTokenIssuer()
    {
        return application.Services.GetRequiredService<WorkerTokenIssuer>();
    }

    /// <summary>Posts a GitHub issue webhook through the real webhook endpoint and returns the run/work-item <c>IntakeRunLauncher</c> created — same mechanism as <see cref="AgentLoopHost.SeedTicketAsync"/>.</summary>
    public async Task<SeededWorkItem> SeedTicketAsync(
        string title,
        string body,
        IReadOnlyList<string> labels,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        var payload = BuildGithubIssuePayload(title, body, labels, issueNumber);
        var deliveryId = "real-pi-" + Guid.NewGuid().ToString("N");
        var signature = "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(HookSecret), payload)).ToLowerInvariant();

        using var anonymous = new HttpClient { BaseAddress = BaseAddress };
        using var content = new ByteArrayContent(payload);
        content.Headers.Add("Content-Type", "application/json");
        content.Headers.Add("X-GitHub-Delivery", deliveryId);
        content.Headers.Add("X-GitHub-Event", "issues");
        content.Headers.Add("X-Hub-Signature-256", signature);

        var response = await anonymous.PostAsync(webhookPath, content, cancellationToken);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        document.RootElement.GetProperty("outcome").GetString().ShouldBe("admitted");

        var externalId = $"comuki/real-pi-fixture#{issueNumber}";
        await using var scope = application.Services.CreateAsyncScope();
        using var systemScope = scope.ServiceProvider.GetRequiredService<ISubjectScopeAccessor>().AsSystem("real-pi-fixture");
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
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
        using var systemScope = scope.ServiceProvider.GetRequiredService<ISubjectScopeAccessor>().AsSystem("real-pi-fixture");
        var journal = scope.ServiceProvider.GetRequiredService<IRunJournal>();
        return await journal.ReadTimelineAsync(new RunId(runId), page: 1, pageSize: 200, cancellationToken);
    }

    /// <summary>Reads the work item's current status string.</summary>
    public async Task<string> ReadWorkItemStatusAsync(Guid workItemId, CancellationToken cancellationToken)
    {
        await using var scope = application.Services.CreateAsyncScope();
        using var systemScope = scope.ServiceProvider.GetRequiredService<ISubjectScopeAccessor>().AsSystem("real-pi-fixture");
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        var item = await db.WorkItems.AsNoTracking().SingleAsync(candidate => candidate.Id == workItemId, cancellationToken);
        return item.Status.ToString();
    }

    /// <summary>One-time source connection + watch admission rule the scenario's webhook lands through.</summary>
    private async Task<string> ProvisionWebhookAsync(CancellationToken cancellationToken)
    {
        using var browser = new HttpClient(new HttpClientHandler { UseCookies = true, CheckCertificateRevocationList = true })
        {
            BaseAddress = BaseAddress,
        };
        await browser.LoginAsBootstrapAdminAsync(cancellationToken);

        var projectId = ProjectId.New().Value;
        var connectionResponse = await browser.PostAsJsonAsync(
            "/api/v1/sources",
            new
            {
                projectId,
                provider = "github",
                name = "real-pi hook",
                settingsJson = /*lang=json,strict*/ """{"owner": "comuki", "repo": "real-pi-fixture"}""",
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
                html_url = $"https://github.com/comuki/real-pi-fixture/issues/{issueNumber}",
                state = "open",
                user = new { login = "real-pi-fixture" },
                labels = labels.Select(static label => new { name = label }).ToArray(),
            },
            repository = new { full_name = "comuki/real-pi-fixture" },
        };
        return JsonSerializer.SerializeToUtf8Bytes(payload, JsonSerializerOptions.Web);
    }
}
