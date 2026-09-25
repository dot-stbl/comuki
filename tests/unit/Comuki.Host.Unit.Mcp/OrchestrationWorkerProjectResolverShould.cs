using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Mcp;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Mcp;

/// <summary>
/// The lease → project resolution behind the worker MCP scope: the
/// resolver finds the project of the run behind the worker's running
/// work item, ignores other workers' items, and answers null when the
/// worker holds nothing.
/// </summary>
public sealed class OrchestrationWorkerProjectResolverShould
{
    [Fact(DisplayName = "Given a worker leasing a running item, when ResolveProjectAsync runs, then the parent run's project comes back")]
    public async Task ResolvesTheLeasedItemsRunProjectAsync()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var workerId = WorkerId.New();
        var accessor = new AsyncLocalSubjectScopeAccessor();
        await using var db = NewDbContext(accessor);
        await SeedLeasedItemAsync(db, projectId, workerId, WorkItemStatus.Running);
        var resolver = new OrchestrationWorkerProjectResolver(db, accessor);

        var resolved = await resolver.ResolveProjectAsync(workerId, TestContext.Current.CancellationToken);

        resolved.ShouldBe(projectId.Value);
    }

    [Fact(DisplayName = "Given another worker's running item, when ResolveProjectAsync runs for a worker with no lease, then null comes back")]
    public async Task AnswersNullForAWorkerWithoutALeaseAsync()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var accessor = new AsyncLocalSubjectScopeAccessor();
        await using var db = NewDbContext(accessor);
        await SeedLeasedItemAsync(db, projectId, WorkerId.New(), WorkItemStatus.Running);
        var resolver = new OrchestrationWorkerProjectResolver(db, accessor);

        var resolved = await resolver.ResolveProjectAsync(WorkerId.New(), TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a completed item the worker once leased, when ResolveProjectAsync runs, then null comes back — released items carry no scope")]
    public async Task AnswersNullForAReleasedItemAsync()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var workerId = WorkerId.New();
        var accessor = new AsyncLocalSubjectScopeAccessor();
        await using var db = NewDbContext(accessor);
        await SeedLeasedItemAsync(db, projectId, workerId, WorkItemStatus.Succeeded);
        var resolver = new OrchestrationWorkerProjectResolver(db, accessor);

        var resolved = await resolver.ResolveProjectAsync(workerId, TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    private static async Task SeedLeasedItemAsync(
        OrchestrationDbContext db,
        ProjectId projectId,
        WorkerId workerId,
        WorkItemStatus finalStatus)
    {
        var now = DateTimeOffset.UtcNow;
        var run = Run.Create(projectId, now);
        var item = WorkItem.Create(run.Id, "implement", "img:digest", "refs/heads/main", /*lang=json,strict*/ """{"goal":"x"}""", WorkItemStatus.Queued, now);
        item.AssignLease(workerId, 1, now.AddMinutes(5), now);
        if (finalStatus == WorkItemStatus.Succeeded)
        {
            item.TransitionTo(WorkItemStatus.Succeeded, now);
        }

        db.Runs.Add(run);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static OrchestrationDbContext NewDbContext(AsyncLocalSubjectScopeAccessor scopeAccessor)
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: $"worker-project-resolver-{Guid.NewGuid():N}")
            .ConfigureWarnings(static warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new OrchestrationDbContext(options, scopeAccessor: scopeAccessor);
    }
}
