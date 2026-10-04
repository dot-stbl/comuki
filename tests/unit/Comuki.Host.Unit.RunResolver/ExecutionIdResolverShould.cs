using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Runs;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.RunResolver;

/// <summary>
/// <see cref="IExecutionIdResolver"/>: the single shared
/// <c>runId → WorkItem → LeasedBy → WorkerId</c> resolver the run-cancel
/// and run-steer endpoints both call through (add-orchestra §1 — Baton,
/// design D3 + specs/session/spec.md Requirement "RunId-to-ExecutionId
/// resolver is shared"). The InMemory provider exercises the read shape;
/// the schema-bound guard (snake_case columns, the work_items row with
/// <c>status = Running</c> + non-null <c>leased_by</c>) is covered by
/// <c>Comuki.Host.Integration.Steer</c> against a real Postgres.
/// </summary>
public sealed class ExecutionIdResolverShould
{
    [Fact(DisplayName = "Given a run with a Running work item leased by a worker, when ResolveAsync is called, then that worker's id is returned")]
    public async Task ResolveSingleRunningItemAsync()
    {
        var db = NewDb();
        var resolver = NewResolver(db);
        var runId = RunId.New();
        var workerId = WorkerId.New();
        await SeedRunAsync(db, runId);
        await SeedItemAsync(db, runId, "implement", WorkItemStatus.Running, workerId, leased: true);

        var resolved = await resolver.ResolveAsync(runId, TestContext.Current.CancellationToken);

        resolved.ShouldBe(workerId);
    }

    [Fact(DisplayName = "Given a run with only a Queued work item, when ResolveAsync is called, then null is returned (no live lease)")]
    public async Task ResolveQueuedOnlyReturnsNullAsync()
    {
        var db = NewDb();
        var resolver = NewResolver(db);
        var runId = RunId.New();
        await SeedRunAsync(db, runId);
        await SeedItemAsync(db, runId, "implement", WorkItemStatus.Queued, workerId: null, leased: false);

        var resolved = await resolver.ResolveAsync(runId, TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a run with no work items at all, when ResolveAsync is called, then null is returned (run just created, nothing claimed)")]
    public async Task ResolveRunWithoutItemsReturnsNullAsync()
    {
        var db = NewDb();
        var resolver = NewResolver(db);
        var runId = RunId.New();
        await SeedRunAsync(db, runId);

        var resolved = await resolver.ResolveAsync(runId, TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a terminal Succeeded run, when ResolveAsync is called, then null is returned (terminal runs have no live lease)")]
    public async Task ResolveTerminalRunReturnsNullAsync()
    {
        var db = NewDb();
        var resolver = NewResolver(db);
        var runId = RunId.New();
        await SeedRunAsync(db, runId, status: RunStatus.Succeeded);
        await SeedItemAsync(db, runId, "implement", WorkItemStatus.Succeeded, workerId: null, leased: false);

        var resolved = await resolver.ResolveAsync(runId, TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a runId that does not exist, when ResolveAsync is called, then null is returned (no exception)")]
    public async Task ResolveUnknownRunReturnsNullAsync()
    {
        var db = NewDb();
        var resolver = NewResolver(db);
        var unknown = RunId.New();

        var resolved = await resolver.ResolveAsync(unknown, TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a run with two Running work items (state corruption), when ResolveAsync is called, then null is returned (defensive)")]
    public async Task ResolveMultipleRunningReturnsNullAsync()
    {
        var db = NewDb();
        var resolver = NewResolver(db);
        var runId = RunId.New();
        var workerA = WorkerId.New();
        var workerB = WorkerId.New();
        await SeedRunAsync(db, runId);
        await SeedItemAsync(db, runId, "implement", WorkItemStatus.Running, workerA, leased: true);
        await SeedItemAsync(db, runId, "implement", WorkItemStatus.Running, workerB, leased: true);

        var resolved = await resolver.ResolveAsync(runId, TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a Run is resolved, when AsSystem(\\\"runs-execution-id-resolver\\\") declares the scope, then the read sees every project row (no project-scope filter applied)")]
    public async Task ResolverDeclaresSystemScopeAsync()
    {
        var db = NewDb();
        var scopeAccessor = Substitute.For<ISubjectScopeAccessor>();
        var resolver = new ExecutionIdResolver(db, scopeAccessor);
        var runId = RunId.New();
        await SeedRunAsync(db, runId);

        await resolver.ResolveAsync(runId, TestContext.Current.CancellationToken);

        // The scope accessor is asked for a system scope by name
        // exactly — that's the contract the seam holds for both the
        // run-cancel and the run-steer caller.
        scopeAccessor.Received().AsSystem("runs-execution-id-resolver");
    }

    private static OrchestrationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: $"run-resolver-{Guid.NewGuid():N}")
            .Options;
        return new OrchestrationDbContext(options, scopeAccessor: null);
    }

    private static IExecutionIdResolver NewResolver(OrchestrationDbContext db)
    {
        var scopeAccessor = Substitute.For<ISubjectScopeAccessor>();
        return new ExecutionIdResolver(db, scopeAccessor);
    }

    private static Task SeedRunAsync(OrchestrationDbContext db, RunId runId)
    {
        return SeedRunAsync(db, runId, RunStatus.Queued);
    }

    private static async Task SeedRunAsync(
        OrchestrationDbContext db,
        RunId runId,
        RunStatus status)
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var run = Run.Create(ProjectId.New(), now);
        // Run.Id is server-assigned; overwrite to match the test seed.
        typeof(Run).GetProperty(nameof(Run.Id))!.SetValue(run, runId);
        // Walk the legal transition table to reach a non-default
        // target — Run.TransitionTo refuses illegal hops, so direct
        // Queued -> Succeeded is rejected by the aggregate guard.
        var path = PathTo(status);
        foreach (var step in path)
        {
            run.TransitionTo(step, now);
        }
        db.Runs.Add(run);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Returns the legal transition path from <c>Queued</c> to
    /// <paramref name="target"/>. The terminal targets each have a
    /// distinct path through the table (see <c>RunTransitions.table</c>);
    /// non-terminal targets land in one hop. <c>Queued</c> itself is the
    /// empty path.
    /// </summary>
    /// <param name="target">Status to seed the run into.</param>
    private static RunStatus[] PathTo(RunStatus target)
    {
        // RunStatus is a readonly record struct whose members are
        // static get-only properties — the C# 9 pattern-match rules
        // reject them in a switch arm. Compare on the wire-form string
        // instead.
        var value = target.Value;
        return value switch
        {
            nameof(RunStatus.Queued) => [],
            nameof(RunStatus.Waiting) => [RunStatus.Waiting],
            nameof(RunStatus.Running) => [RunStatus.Running],
            nameof(RunStatus.Escalated) => [RunStatus.Escalated],
            nameof(RunStatus.Failed) => [RunStatus.Failed],
            nameof(RunStatus.Succeeded) => [RunStatus.Running, RunStatus.Succeeded],
            nameof(RunStatus.Cancelled) => [RunStatus.Cancelled],
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };
    }

    private static async Task SeedItemAsync(
        OrchestrationDbContext db,
        RunId runId,
        string profileKey,
        WorkItemStatus status,
        WorkerId? workerId,
        bool leased)
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var item = WorkItem.Create(
            runId,
            profileKey,
            "ghcr.io/comuki/worker:dev",
            "net10-sdk-bun",
            "refs/heads/main",
                                 /*lang=json,strict*/
                                 "{\"Goal\":\"seed\"}",
            WorkItemStatus.Queued,
            now);
        if (status == WorkItemStatus.Running && leased && workerId is { } wid)
        {
            item.AssignLease(wid, 1, now.AddMinutes(5), now);
        }
        db.WorkItems.Add(item);
        await db.SaveChangesAsync();
    }
}
