using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Chat.RunStarter;
using Comuki.Shared.Bootstrap.Versioning;
using Comuki.Shared.Contracts.Plans;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

using ChatRunStarterService = Comuki.Host.Chat.RunStarter.ChatRunStarter;

namespace Comuki.Host.Unit.ChatRunStarter;

/// <summary>
/// <see cref="ChatRunStarter"/>: turns an approved <see cref="Plan"/> into
/// one run and the run's work items, honoring the plan DAG as the source of
/// <c>Blocked</c> vs <c>Queued</c>. Persistence is exercised through EF
/// Core's InMemory provider — production schema-per-history assertions
/// (Npgsql + <c>useSnakeCaseNamingConvention</c>) live with the rest of
/// <see cref="OrchestrationDbContext"/> contract testing. The DB-bound
/// paths (claim/unblock/reaper) are covered by
/// <c>Comuki.Host.Integration.Chat</c> against a real Postgres.
/// </summary>
public sealed class ChatRunStarterShould
{
    [Fact(DisplayName = "Given a single-node plan with no edges, when started, then the item is created Queued")]
    public async Task SingleNodeNoEdgesCreatesQueuedItemAsync()
    {
        var db = NewDb();
        var starter = NewStarter(db);
        var plan = new Plan(
            Summary: "single step",
            Nodes: [new PlanNode("n1", "only step", "implement", "do the first thing")],
            Edges: []);

        await starter.StartAsync(ProjectId.New(), plan, TestContext.Current.CancellationToken);

        var items = await db.WorkItems.ToListAsync(TestContext.Current.CancellationToken);
        items.ShouldHaveSingleItem();
        items[0].Status.ShouldBe(WorkItemStatus.Queued);
    }

    [Fact(DisplayName = "Given a two-node plan with one edge n1->n2, when started, then n1 (no prerequisite) is Queued and n2 (has a prerequisite) is Blocked")]
    public async Task TwoNodesOneEdgeCreatesQueuedThenBlockedAsync()
    {
        var db = NewDb();
        var starter = NewStarter(db);
        var plan = new Plan(
            Summary: "linear pair",
            Nodes:
            [
                new PlanNode("n1", "first", "implement", "do the first thing"),
                new PlanNode("n2", "second", "implement", "do the second thing"),
            ],
            Edges: [new PlanEdge("n1", "n2")]);

        await starter.StartAsync(ProjectId.New(), plan, TestContext.Current.CancellationToken);

        var items = await db.WorkItems.ToListAsync(TestContext.Current.CancellationToken);
        items.Count.ShouldBe(2);

        var first = items.Single(static item => item.Brief.Contains("do the first thing"));
        var second = items.Single(static item => item.Brief.Contains("do the second thing"));
        first.Status.ShouldBe(WorkItemStatus.Queued);
        second.Status.ShouldBe(WorkItemStatus.Blocked);

        var dependencies = await db.WorkItemDependencies.ToListAsync(TestContext.Current.CancellationToken);
        dependencies.ShouldHaveSingleItem();
        dependencies[0].WorkItemId.ShouldBe(second.Id);
        dependencies[0].DependsOnWorkItemId.ShouldBe(first.Id);
    }

    [Fact(DisplayName = "Given a node with two prerequisites (a diamond), when started, then the dependent is Blocked and both prerequisites are Queued")]
    public async Task DiamondPlanBlocksOnlyTheApexAsync()
    {
        var db = NewDb();
        var starter = NewStarter(db);
        var plan = new Plan(
            Summary: "diamond",
            Nodes:
            [
                new PlanNode("n1", "left prerequisite", "implement", "do the left thing"),
                new PlanNode("n2", "right prerequisite", "implement", "do the right thing"),
                new PlanNode("n3", "apex", "implement", "do the join thing"),
            ],
            Edges:
            [
                new PlanEdge("n1", "n3"),
                new PlanEdge("n2", "n3"),
            ]);

        await starter.StartAsync(ProjectId.New(), plan, TestContext.Current.CancellationToken);

        var items = await db.WorkItems.ToListAsync(TestContext.Current.CancellationToken);
        items.Count.ShouldBe(3);

        var left = items.Single(static item => item.Brief.Contains("do the left thing"));
        var right = items.Single(static item => item.Brief.Contains("do the right thing"));
        var apex = items.Single(static item => item.Brief.Contains("do the join thing"));

        left.Status.ShouldBe(WorkItemStatus.Queued);
        right.Status.ShouldBe(WorkItemStatus.Queued);
        apex.Status.ShouldBe(WorkItemStatus.Blocked);
    }

    [Fact(DisplayName = "Given a three-node chain n1->n2->n3, when started, then only n1 (the root) is Queued and both n2 and n3 are Blocked")]
    public async Task ThreeNodeChainBlocksEveryNonRootAsync()
    {
        var db = NewDb();
        var starter = NewStarter(db);
        var plan = new Plan(
            Summary: "chain",
            Nodes:
            [
                new PlanNode("n1", "root", "implement", "do the first thing"),
                new PlanNode("n2", "middle", "implement", "do the second thing"),
                new PlanNode("n3", "leaf", "implement", "do the third thing"),
            ],
            Edges:
            [
                new PlanEdge("n1", "n2"),
                new PlanEdge("n2", "n3"),
            ]);

        await starter.StartAsync(ProjectId.New(), plan, TestContext.Current.CancellationToken);

        var items = await db.WorkItems.ToListAsync(TestContext.Current.CancellationToken);
        items.Count.ShouldBe(3);

        var root = items.Single(static item => item.Brief.Contains("do the first thing"));
        var middle = items.Single(static item => item.Brief.Contains("do the second thing"));
        var leaf = items.Single(static item => item.Brief.Contains("do the third thing"));

        root.Status.ShouldBe(WorkItemStatus.Queued);
        middle.Status.ShouldBe(WorkItemStatus.Blocked);
        leaf.Status.ShouldBe(WorkItemStatus.Blocked);
    }

    [Fact(DisplayName = "Given a plan, when started, then it returns the created run's id and every created item's RunId matches it")]
    public async Task StartAsyncReturnValueMatchesPersistedRunAndItemsAsync()
    {
        var db = NewDb();
        var starter = NewStarter(db);
        var plan = new Plan(
            Summary: "simple",
            Nodes:
            [
                new PlanNode("n1", "first", "implement", "do the first thing"),
                new PlanNode("n2", "second", "implement", "do the second thing"),
            ],
            Edges: [new PlanEdge("n1", "n2")]);

        var runId = await starter.StartAsync(ProjectId.New(), plan, TestContext.Current.CancellationToken);

        var persistedRun = await db.Runs.SingleAsync(TestContext.Current.CancellationToken);
        persistedRun.Id.ShouldBe(runId);

        var items = await db.WorkItems.ToListAsync(TestContext.Current.CancellationToken);
        items.ShouldAllBe(item => item.RunId == runId);
    }

    private static OrchestrationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: $"chat-run-starter-{Guid.NewGuid():N}")
            .Options;
        return new OrchestrationDbContext(options, scopeAccessor: null);
    }

    private static ChatRunStarterService NewStarter(OrchestrationDbContext db)
    {
        return new ChatRunStarterService(
            db,
            Options.Create(new ChatWorkerDefaults()),
            ComukiBuildInformation.Unknown,
            TimeProvider.System);
    }
}
