using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Runs;
using Comuki.Host.Workers.Grpc;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Realtime;

/// <summary>
/// Host adapter unit tests for the run decision endpoints
/// (<c>HostApproveRunAdapter</c>, <c>HostCancelRunAdapter</c>). The two
/// adapters are thin: they wrap <see cref="RunTransitions.IsLegal"/>
/// behind <see cref="OrchestrationDbContext"/> + journal append, and
/// emit a typed exception on illegal transitions. The tests exercise
/// the contract through an in-memory <see cref="OrchestrationDbContext"/>
/// rather than a real Testcontainers Postgres so the transitions +
/// journal row assertions stay close to the adapter code.
/// </summary>
public sealed class RunDecisionAdaptersShould
{
    [Fact(DisplayName = "Given an escalated run, when approve runs, then status becomes Running and a run.status_changed event is appended")]
    public async Task ApproveTransitionsEscalatedToRunningAndAppendsJournalEventAsync()
    {
        var db = await NewDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var run = await SeedRunAsync(db, RunStatus.Escalated, now);

        var adapter = new HostApproveRunAdapter(db, NewScopeAccessor(), new FixedClock(now));

        await adapter.ApproveAsync(run.Id, CancellationToken.None);

        var stored = db.Runs.Single();
        stored.Status.ShouldBe(RunStatus.Running);

        var entry = db.RunEvents.Single();
        entry.Type.ShouldBe(RunEventTypes.RunStatusChanged);
        entry.RunId.ShouldBe(run.Id);
        entry.Payload.ShouldContain("\"from\":\"Escalated\"");
        entry.Payload.ShouldContain("\"to\":\"Running\"");
    }

    [Fact(DisplayName = "Given a succeeded run, when approve runs, then it throws RunDecisionConflictException and persists nothing")]
    public async Task ApproveOnTerminalRunThrowsAsync()
    {
        var db = await NewDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var run = await SeedRunAsync(db, RunStatus.Succeeded, now);

        var adapter = new HostApproveRunAdapter(db, NewScopeAccessor(), new FixedClock(now));

        var exception = await Should.ThrowAsync<RunDecisionConflictException>(
            () => adapter.ApproveAsync(run.Id, CancellationToken.None));

        exception.Current.ShouldBe(RunStatus.Succeeded);
        exception.Requested.ShouldBe(RunStatus.Running);
        exception.Decision.ShouldBe("approve");
        db.RunEvents.ShouldBeEmpty();
    }

    [Fact(
        Skip = "Cancel fences via relational SQL (HostCancelRunAdapter CAS); InMemory cannot host GetDbTransaction. Covered by Host.Integration.Runs.",
        DisplayName = "Given a queued run, when cancel runs with a reason, then status becomes Cancelled and the reason rides in the journal payload")]
    public async Task CancelWithReasonPersistsReasonOnJournalEntryAsync()
    {
        var db = await NewDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var run = await SeedRunAsync(db, RunStatus.Queued, now);

        var adapter = new HostCancelRunAdapter(
            db,
            NewScopeAccessor(),
            new FixedClock(now),
            NewCommandPipeStub(delivered: true),
            NullLogger<HostCancelRunAdapter>.Instance);

        await adapter.CancelAsync(run.Id, "operator out of office", CancellationToken.None);

        var stored = db.Runs.Single();
        stored.Status.ShouldBe(RunStatus.Cancelled);

        var entry = db.RunEvents.Single();
        entry.Type.ShouldBe(RunEventTypes.RunStatusChanged);
        entry.Payload.ShouldContain("operator out of office");
    }

    [Fact(DisplayName = "Given a cancelled run, when cancel runs, then it throws RunDecisionConflictException")]
    public async Task CancelOnTerminalRunThrowsAsync()
    {
        var db = await NewDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var run = await SeedRunAsync(db, RunStatus.Cancelled, now);

        var adapter = new HostCancelRunAdapter(
            db,
            NewScopeAccessor(),
            new FixedClock(now),
            NewCommandPipeStub(delivered: true),
            NullLogger<HostCancelRunAdapter>.Instance);

        var exception = await Should.ThrowAsync<RunDecisionConflictException>(
            () => adapter.CancelAsync(run.Id, null, CancellationToken.None));

        exception.Current.ShouldBe(RunStatus.Cancelled);
        exception.Decision.ShouldBe("cancel");
    }

    [Fact(DisplayName = "Given a mix of Running and Queued items under a run, when the cancel fanout runs, then TrySendStop is called once per Running item's worker and never for Queued")]
    public async Task FanOutStopTargetsOnlyLiveWorkersAsync()
    {
        var db = await NewDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var run = await SeedRunAsync(db, RunStatus.Queued, now);

        var runningWorker1 = WorkerId.New();
        var runningWorker2 = WorkerId.New();
        await SeedItemAsync(db, run.Id, WorkItemStatus.Running, leasedBy: runningWorker1, generation: 1);
        await SeedItemAsync(db, run.Id, WorkItemStatus.Running, leasedBy: runningWorker2, generation: 1);
        await SeedItemAsync(db, run.Id, WorkItemStatus.Queued, leasedBy: null, generation: 1);
        await SeedItemAsync(db, run.Id, WorkItemStatus.Queued, leasedBy: null, generation: 2);

        var commandPipe = Substitute.For<IWorkerCommandPipe>();
        commandPipe.TrySendStop(Arg.Any<WorkerId>(), Arg.Any<string>()).Returns(true);

        // The InMemory DbContext cannot host the transactional CAS
        // path that HostCancelRunAdapter.CancelAsync opens first; we
        // exercise the post-fence fanout seam directly, which is the
        // load-bearing shape under test. The full adapter integration
        // is the existing skipped test + the integration suite
        // (Host.Integration.Runs).
        await CancelTransition.FanOutStopToLiveWorkersAsync(
            db,
            run.Id,
            commandPipe,
            NullLogger.Instance,
            CancellationToken.None);

        commandPipe.Received(1).TrySendStop(
            Arg.Is<WorkerId>(worker => worker == runningWorker1),
            Arg.Any<string>());
        commandPipe.Received(1).TrySendStop(
            Arg.Is<WorkerId>(worker => worker == runningWorker2),
            Arg.Any<string>());
        commandPipe.DidNotReceive().TrySendStop(
            Arg.Is<WorkerId>(worker => worker != runningWorker1 && worker != runningWorker2),
            Arg.Any<string>());
    }

    [Fact(DisplayName = "Given a quiet worker (no live stream), when the cancel fanout runs, then the miss is silent — no exception, the fence on which the worker discovers the cancel later is enough")]
    public async Task FanOutStopMissIsNotAnErrorAsync()
    {
        var db = await NewDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var run = await SeedRunAsync(db, RunStatus.Queued, now);
        var quietWorker = WorkerId.New();
        await SeedItemAsync(db, run.Id, WorkItemStatus.Running, leasedBy: quietWorker, generation: 1);

        var commandPipe = Substitute.For<IWorkerCommandPipe>();
        commandPipe.TrySendStop(Arg.Any<WorkerId>(), Arg.Any<string>()).Returns(false);

        // The miss must not throw — quiet workers discover the cancel
        // on their next heartbeat via the generation fence.
        await CancelTransition.FanOutStopToLiveWorkersAsync(
            db,
            run.Id,
            commandPipe,
            NullLogger.Instance,
            CancellationToken.None);

        commandPipe.Received(1).TrySendStop(quietWorker, Arg.Any<string>());
    }

    private static ISubjectScopeAccessor NewScopeAccessor()
    {
        var accessor = Substitute.For<ISubjectScopeAccessor>();
        accessor.AsSystem(Arg.Any<string>()).Returns(static _ => new NoOpScope());
        return accessor;
    }

    private static IWorkerCommandPipe NewCommandPipeStub(bool delivered)
    {
        var stub = Substitute.For<IWorkerCommandPipe>();
        stub.TrySendStop(Arg.Any<WorkerId>(), Arg.Any<string>()).Returns(delivered);
        return stub;
    }

    /// <summary>
    /// Seeds one work item directly under the given run, walking the
    /// real domain factory: <see cref="WorkItem.Create"/> for the
    /// Queued seed, then <see cref="WorkItem.AssignLease"/> to move
    /// the Running variant to its leased state. The cancel tests only
    /// need rows of the right shape (status + leased_by) to exercise
    /// the fanout query.
    /// </summary>
    private static async Task SeedItemAsync(
        OrchestrationDbContext db,
        RunId runId,
        WorkItemStatus status,
        WorkerId? leasedBy,
        int generation)
    {
        var now = DateTimeOffset.UtcNow;
        var item = WorkItem.Create(
            runId,
            profileKey: "implement",
            image: "run-cell",
            envClass: "net10-sdk-bun",
            profilesRef: "refs/heads/main",
            brief: "do the thing",
            initialStatus: WorkItemStatus.Queued,
            now: now);

        if (status == WorkItemStatus.Running && leasedBy is { } worker)
        {
            item.AssignLease(worker, generation, leaseUntil: now.AddMinutes(2), now: now);
        }
        else if (status != WorkItemStatus.Queued)
        {
            throw new ArgumentException(
                $"SeedItemAsync only knows Queued and Running (via AssignLease); got {status}.",
                nameof(status));
        }

        db.WorkItems.Add(item);
        await db.SaveChangesAsync();
    }

    private static async Task<OrchestrationDbContext> NewDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: $"runs-decisions-{Guid.NewGuid()}")
            .ConfigureWarnings(static warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new OrchestrationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static async Task<Run> SeedRunAsync(OrchestrationDbContext db, RunStatus status, DateTimeOffset at)
    {
        var run = Run.Create(ProjectId.New(), at);
        // Walk a legal chain from Queued to the target status — the
        // aggregate's transition guard only accepts table-driven edges, so
        // direct seeds are not possible for terminal targets (e.g. Succeeded).
        var chain = ResolveRunStatusChain(status);

        var step = at.AddSeconds(1);
        foreach (var hop in chain)
        {
            run.TransitionTo(hop, step);
            step = step.AddSeconds(1);
        }

        db.Runs.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    /// <summary>
    /// Map a target <see cref="RunStatus"/> to the legal sequence of
    /// transitions needed to reach it from <see cref="RunStatus.Queued"/>.
    /// Plain if-chain because smart-type members are static properties
    /// and switch-expression arm patterns require constants.
    /// </summary>
    /// <param name="status"></param>
    private static IReadOnlyList<RunStatus> ResolveRunStatusChain(RunStatus status)
    {
        if (status == RunStatus.Queued)
        {
            return [];
        }

        if (status == RunStatus.Waiting)
        {
            return [RunStatus.Waiting];
        }

        if (status == RunStatus.Running)
        {
            return [RunStatus.Running];
        }

        if (status == RunStatus.Succeeded)
        {
            return [RunStatus.Running, RunStatus.Succeeded];
        }

        if (status == RunStatus.Failed)
        {
            return [RunStatus.Failed];
        }

        if (status == RunStatus.Cancelled)
        {
            return [RunStatus.Cancelled];
        }

        if (status == RunStatus.Escalated)
        {
            return [RunStatus.Running, RunStatus.Escalated];
        }

#pragma warning disable IDE0046
        throw new ArgumentOutOfRangeException(nameof(status), status, null);
#pragma warning restore IDE0046
    }
}

/// <summary>Fixed clock — deterministic stamps for journal rows.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return now;
    }
}

/// <summary>Disposable stub the scope accessor hands back — no-op for unit tests.</summary>
internal sealed class NoOpScope : IDisposable
{
    public void Dispose()
    {
    }
}
