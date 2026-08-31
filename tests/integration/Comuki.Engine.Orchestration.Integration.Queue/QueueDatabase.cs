using Comuki.Engine.Orchestration.Application;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Contracts.Queue;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Engine.Orchestration.Integration.Queue;

/// <summary>
/// Real-Postgres base for the queue suite: one container per test (fresh
/// schema, no cross-test rows), the real installers on an in-memory
/// configuration, and a deterministic clock so leases expire by advancing
/// time instead of sleeping.
/// </summary>
public abstract class QueueDatabase : IAsyncLifetime
{
    protected const string Image = "ghcr.io/comuki/worker@sha256:9f86d0";
    protected const string ProfilesRef = "refs/heads/main";

    /// <summary>Default claim labels the seeded items match on.</summary>
    protected static WorkItemLabels ImplementLabels => new(Image, ProfilesRef, "implement");

    /// <summary>
    /// boundary: initialised in InitializeAsync before any test runs
    /// </summary>
    protected readonly FakeTimeProvider clock = new();

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    /// <summary>
    /// boundary: initialised in InitializeAsync before any test runs
    /// </summary>
    private ServiceProvider provider = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await container.StartAsync(TestContext.Current.CancellationToken);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestration:Lease:LeaseTtl"] = "00:02:00",
                ["Orchestration:Lease:ReapGrace"] = "00:00:30",
                ["Orchestration:Lease:MaxAttempts"] = "2",
                ["Orchestration:Lease:ReapInterval"] = "01:00:00",
            })
            .Build();

        var services = new ServiceCollection();
        _ = services.AddSingleton<TimeProvider>(clock);
        _ = services.AddOrchestrationPersistence(container.GetConnectionString());
        _ = services.AddOrchestrationQueue(configuration);
        _ = services.AddOrchestrationApplication();
        provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        await container.DisposeAsync();
    }

    /// <summary>A fresh DI scope — one scope = one DbContext, the unit of concurrency tests.</summary>
    protected IServiceScope CreateScope()
    {
        return provider.CreateScope();
    }

    /// <summary>Seeds a run row (journal appends are FK-bound to runs).</summary>
    protected async Task<Run> SeedRunAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        var run = Run.Create(ProjectId.New(), clock.GetUtcNow());
        _ = db.Runs.Add(run);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return run;
    }

    /// <summary>Seeds a run with one queued item for the default implement labels.</summary>
    /// <param name="profileKey"></param>
    protected async Task<WorkItem> SeedQueuedItemAsync(string profileKey = "implement")
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        var now = clock.GetUtcNow();
        var run = Run.Create(ProjectId.New(), now);
        var item = WorkItem.Create(
            run.Id, profileKey, Image, ProfilesRef, /*lang=json,strict*/ """{"goal":"do the thing"}""", WorkItemStatus.Queued, now);

        _ = db.Runs.Add(run);
        _ = db.WorkItems.Add(item);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return item;
    }

    /// <summary>Re-reads one work item from a fresh scope (no tracking).</summary>
    /// <param name="workItemId"></param>
    protected async Task<WorkItem?> LoadItemAsync(Guid workItemId)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        return await db.WorkItems.AsNoTracking().SingleOrDefaultAsync(item => item.Id == workItemId, TestContext.Current.CancellationToken);
    }

    /// <summary>Re-reads the journal of a run from a fresh scope (no tracking).</summary>
    /// <param name="runId"></param>
    protected async Task<List<RunEvent>> LoadEventsAsync(RunId runId)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        return await db.RunEvents.AsNoTracking()
            .Where(runEvent => runEvent.RunId == runId)
            .OrderBy(runEvent => runEvent.OccurredAt)
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
