using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Runs;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Realtime;

/// <summary>
/// Unit tests for the <see cref="GetRunDetailHandler"/> behind
/// <c>GET /api/v1/runs/{runId}</c>. The handler is host-internal: it reads
/// the orchestration context with <c>AsNoTracking()</c>, builds the wire
/// envelope, and returns <c>null</c> when the row is not visible (out of
/// scope or truly absent). Tests exercise the contract through an
/// in-memory <see cref="OrchestrationDbContext"/> so the projection +
/// journal row + dependency joins stay close to the handler code.
/// </summary>
public sealed class GetRunDetailHandlerShould
{
    [Fact(DisplayName = "Given a run with no work items and no events, when read, then the wire detail carries the core fields and empty lists")]
    public async Task ReturnDetailForEmptyRunAsync()
    {
        var db = await NewDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var run = await SeedRunAsync(db, RunStatus.Queued, now);

        var handler = new GetRunDetailHandler(db);

        var detail = await handler.GetAsync(run.Id, TestContext.Current.CancellationToken);

        detail.ShouldNotBeNull();
        detail.Id.ShouldBe(run.Id.Value);
        detail.ProjectId.ShouldBe(run.ProjectId.Value);
        detail.Status.ShouldBe("queued");
        detail.CreatedAt.ShouldBe(now);
        detail.UpdatedAt.ShouldBe(now);
        detail.WorkItems.ShouldBeEmpty();
        detail.Events.ShouldBeEmpty();
        detail.Rules.ShouldBeEmpty();
        detail.Brief.ShouldBeEmpty();
        detail.Title.ShouldBeEmpty();
        detail.App.ShouldBeEmpty();
        detail.Revision.Rules.ShouldBeEmpty();
        detail.Revision.Sdk.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a run with one work item, when read, then the work item rides the wire with its dependency list")]
    public async Task ReturnDetailWithWorkItemAndDependenciesAsync()
    {
        var db = await NewDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var run = await SeedRunAsync(db, RunStatus.Running, now);
        var firstItem = await SeedWorkItemAsync(
                    db, run.Id, profileKey: "implement", image: "comuki/worker:1.0.0",
                    profilesRef: "rules@a1b9e0", brief: /*lang=json,strict*/ "{\"goal\":\"ship it\"}");
        var secondItem = await SeedWorkItemAsync(
            db, run.Id, profileKey: "verify", image: "comuki/worker:1.0.0",
            profilesRef: "rules@a1b9e0", brief: /*lang=json,strict*/ "{\"gate\":\"tests\"}");
        await db.WorkItemDependencies.AddAsync(
            WorkItemDependency.Create(secondItem.Id, firstItem.Id), TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetRunDetailHandler(db);

        var detail = await handler.GetAsync(run.Id, TestContext.Current.CancellationToken);

        detail.ShouldNotBeNull();
        detail.WorkItems.Count.ShouldBe(2);
        detail.WorkItems[0].Id.ShouldBe(firstItem.Id);
        detail.WorkItems[0].Profile.ShouldBe("implement");
        detail.WorkItems[0].DependsOn.ShouldBeEmpty();
        detail.WorkItems[1].Id.ShouldBe(secondItem.Id);
        detail.WorkItems[1].DependsOn.ShouldBe([firstItem.Id]);
        detail.Brief.ShouldBe(/*lang=json,strict*/ "{\"goal\":\"ship it\"}");
        detail.App.ShouldBe("comuki/worker:1.0.0");
        detail.Revision.Rules.ShouldBe("rules@a1b9e0");
        detail.Revision.Sdk.ShouldBe("comuki/worker:1.0.0");
    }

    [Fact(DisplayName = "Given a run with many journal entries, when read, then the events strip carries the most recent twenty in descending order")]
    public async Task CapEventsAtTwentyAndOrderNewestFirstAsync()
    {
        var db = await NewDbContextAsync();
        var start = DateTimeOffset.UtcNow;
        var run = await SeedRunAsync(db, RunStatus.Running, start);

        for (var index = 0; index < 25; index++)
        {
            var stamp = start.AddSeconds(index);
            db.RunEvents.Add(RunEvent.Create(
                run.Id,
                RunEventTypes.WorkItemStatusChanged,
                $"{{\"sequence\":{index}}}",
                stamp));
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetRunDetailHandler(db);

        var detail = await handler.GetAsync(run.Id, TestContext.Current.CancellationToken);

        detail.ShouldNotBeNull();
        detail!.Events.Count.ShouldBe(20);
        detail.Events[0].PayloadJson!.ShouldContain("\"sequence\":24");
        detail.Events[19].PayloadJson!.ShouldContain("\"sequence\":5");
    }

    [Fact(DisplayName = "Given a missing run id, when read, then the handler returns null")]
    public async Task ReturnNullWhenRunDoesNotExistAsync()
    {
        var db = await NewDbContextAsync();

        var handler = new GetRunDetailHandler(db);

        var detail = await handler.GetAsync(RunId.New(), TestContext.Current.CancellationToken);

        detail.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a run with status transitions, when read, then the wire status carries the current lower-case form")]
    public async Task ReturnCurrentStatusAsWireStringAsync()
    {
        var db = await NewDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var run = await SeedRunAsync(db, RunStatus.Succeeded, now);

        var handler = new GetRunDetailHandler(db);

        var detail = await handler.GetAsync(run.Id, TestContext.Current.CancellationToken);

        detail!.Status.ShouldBe("succeeded");
    }

    [Fact(DisplayName = "Given a run with work items that have no dependency edges, when read, then each item's depends-on list is empty")]
    public async Task EmptyDependenciesWhenNoneRecordedAsync()
    {
        var db = await NewDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var run = await SeedRunAsync(db, RunStatus.Running, now);
        await SeedWorkItemAsync(
                    db, run.Id, profileKey: "explore-readonly",
                    image: "comuki/worker:2.0.0", profilesRef: "rules@main",
                    brief: /*lang=json,strict*/ "{\"scope\":\"read-only\"}");

        var handler = new GetRunDetailHandler(db);

        var detail = await handler.GetAsync(run.Id, TestContext.Current.CancellationToken);

        detail!.WorkItems.Count.ShouldBe(1);
        detail.WorkItems[0].DependsOn.ShouldBeEmpty();
    }

    private static async Task<OrchestrationDbContext> NewDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: $"runs-detail-{Guid.NewGuid()}")
            .Options;
        var context = new OrchestrationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static async Task<Run> SeedRunAsync(OrchestrationDbContext db, RunStatus status, DateTimeOffset at)
    {
        var run = Run.Create(ProjectId.New(), at);
        var chain = status switch
        {
            RunStatus.Queued => [],
            RunStatus.Waiting => new[] { RunStatus.Waiting },
            RunStatus.Running => [RunStatus.Running],
            RunStatus.Succeeded => [RunStatus.Running, RunStatus.Succeeded],
            RunStatus.Failed => [RunStatus.Failed],
            RunStatus.Cancelled => [RunStatus.Cancelled],
            RunStatus.Escalated => [RunStatus.Running, RunStatus.Escalated],
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

        var step = at.AddSeconds(1);
        foreach (var hop in chain)
        {
            run.TransitionTo(hop, step);
            step = step.AddSeconds(1);
        }

        db.Runs.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return run;
    }

    private static async Task<WorkItem> SeedWorkItemAsync(
            OrchestrationDbContext db,
            RunId runId,
            string profileKey,
            string image,
            string profilesRef,
            string brief)
    {
        var item = WorkItem.Create(
            runId,
            profileKey,
            image,
            profilesRef,
            brief,
            WorkItemStatus.Queued,
            DateTimeOffset.UtcNow);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return item;
    }
}

