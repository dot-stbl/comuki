using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Comuki.Engine.Compute.Security;
using Comuki.Engine.Orchestration.Application;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Testing;
using Comuki.Host.Testing.Fixtures;
using Comuki.Host.Workers;
using Comuki.Host.Workers.Api;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Workers;

/// <summary>
/// WS4 acceptance (execution-spine-orchestration, issue #87): the claim
/// response surfaces the claimed generation and heartbeat/complete echo
/// it back; a stale generation is rejected 409 work-item.not-owner
/// exactly like an ownership miss, while the current generation still
/// succeeds. Boots the real host composition PLUS the worker REST/gRPC
/// runtime, which HostComposer does not map by default (see
/// WorkerUploadArtifactShould for the same wiring), against the
/// collection's shared, migrated Postgres.
/// </summary>
[Collection(nameof(WorkersIntegrationCollection))]
public sealed class WorkerGenerationFencingShould(PostgresCollectionFixture postgres) : IAsyncLifetime
{
    private const string Image = "ghcr.io/comuki/worker:test";
    private const string ProfilesRef = "main";
    private const string ProfileKey = "implement";

    private WebApplication application = null!;
    private TempControlPlaneRoot controlPlane = null!;
    private Uri baseAddress = null!;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await postgres.ResetDatabaseAsync();
        var connectionString = postgres.ConnectionString;

        var builder = TestHostBuilder.Create(connectionString);
        controlPlane = new TempControlPlaneRoot("worker-generation");
        builder.Configuration["ControlPlane:Root"] = controlPlane.Root;
        TestBootstrapAdmin.Configure(builder.Configuration);
        TestArtifactsSecrets.ApplyPlaceholder(builder.Configuration);

        builder.Services
            .AddOrchestrationApplication()
            .AddWorkerRuntime(builder.Configuration);

        application = await HostComposer.ComposeAsync(builder, HostDatabase.Explicit(connectionString));
        application.MapWorkerRuntime();
        baseAddress = await TestHostBuilder.StartAsync(application, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await application.DisposeAsync();
        controlPlane.Dispose();
    }

    private async Task SeedQueuedItemAsync(CancellationToken cancellationToken)
    {
        using var scope = application.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var run = Run.Create(ProjectId.New(), now);
        var item = WorkItem.Create(run.Id, ProfileKey, Image, ProfilesRef, /*lang=json,strict*/ """{"goal":"x"}""", WorkItemStatus.Queued, now);
        db.Runs.Add(run);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);
    }

    private HttpClient CreateWorkerClient()
    {
        var workerId = WorkerId.New();
        var token = application.Services.GetRequiredService<WorkerTokenIssuer>().Issue(workerId);
        var client = new HttpClient { BaseAddress = baseAddress };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact(DisplayName = "Given a claimed item, when completed at a stale generation, then 409; the current generation still succeeds")]
    public async Task RejectStaleGenerationOnCompleteAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedQueuedItemAsync(cancellationToken);
        using var client = CreateWorkerClient();

        var claimResponse = await client.PostAsJsonAsync("/workers/claim", new ClaimWorkItemRequest(Image, ProfilesRef, ProfileKey), cancellationToken);
        claimResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var claimed = (await claimResponse.Content.ReadFromJsonAsync<ClaimedWorkItemResponse>(cancellationToken)).ShouldNotBeNull();
        claimed.Generation.ShouldBeGreaterThan(0);

        var staleComplete = await client.PostAsJsonAsync(
            $"/workers/{claimed.WorkItemId}/complete",
            new CompleteWorkItemRequest(/*lang=json,strict*/ """{"summary":"stale"}""", claimed.Generation + 1),
            cancellationToken);
        staleComplete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var staleBody = await staleComplete.Content.ReadAsStringAsync(cancellationToken);
        staleBody.ShouldContain("work-item.not-owner");

        var currentComplete = await client.PostAsJsonAsync(
            $"/workers/{claimed.WorkItemId}/complete",
            new CompleteWorkItemRequest(/*lang=json,strict*/ """{"summary":"done"}""", claimed.Generation),
            cancellationToken);
        currentComplete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact(DisplayName = "Given a claimed item, when heartbeated at a stale generation, then 409; the current generation still succeeds")]
    public async Task RejectStaleGenerationOnHeartbeatAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedQueuedItemAsync(cancellationToken);
        using var client = CreateWorkerClient();

        var claimResponse = await client.PostAsJsonAsync("/workers/claim", new ClaimWorkItemRequest(Image, ProfilesRef, ProfileKey), cancellationToken);
        var claimed = (await claimResponse.Content.ReadFromJsonAsync<ClaimedWorkItemResponse>(cancellationToken)).ShouldNotBeNull();

        var staleHeartbeat = await client.PostAsJsonAsync(
            $"/workers/{claimed.WorkItemId}/heartbeat",
            new HeartbeatWorkItemRequest(claimed.Generation + 1),
            cancellationToken);
        staleHeartbeat.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var currentHeartbeat = await client.PostAsJsonAsync(
            $"/workers/{claimed.WorkItemId}/heartbeat",
            new HeartbeatWorkItemRequest(claimed.Generation),
            cancellationToken);
        currentHeartbeat.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact(DisplayName = "Given a claimed item, when heartbeated with no request body (a pre-WS4/WS5 Translator), then 409 not-owner, not 400")]
    public async Task RejectMissingBodyOnHeartbeatAsGenerationMissAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedQueuedItemAsync(cancellationToken);
        using var client = CreateWorkerClient();

        var claimResponse = await client.PostAsJsonAsync("/workers/claim", new ClaimWorkItemRequest(Image, ProfilesRef, ProfileKey), cancellationToken);
        var claimed = (await claimResponse.Content.ReadFromJsonAsync<ClaimedWorkItemResponse>(cancellationToken)).ShouldNotBeNull();

        // No body at all — a pre-WS4/WS5 Translator's exact shape. Must bind
        // to a null request (generation defaults to 0, never a real claimed
        // generation) and 409, not fail model binding with a 400.
        var bodylessHeartbeat = await client.PostAsync($"/workers/{claimed.WorkItemId}/heartbeat", content: null, cancellationToken);

        bodylessHeartbeat.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = await bodylessHeartbeat.Content.ReadAsStringAsync(cancellationToken);
        body.ShouldContain("work-item.not-owner");
    }
}
