using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Runs;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
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

    [Fact(DisplayName = "Given a queued run, when cancel runs with a reason, then status becomes Cancelled and the reason rides in the journal payload")]
    public async Task CancelWithReasonPersistsReasonOnJournalEntryAsync()
    {
        var db = await NewDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var run = await SeedRunAsync(db, RunStatus.Queued, now);

        var adapter = new HostCancelRunAdapter(db, NewScopeAccessor(), new FixedClock(now));

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

        var adapter = new HostCancelRunAdapter(db, NewScopeAccessor(), new FixedClock(now));

        var exception = await Should.ThrowAsync<RunDecisionConflictException>(
            () => adapter.CancelAsync(run.Id, null, CancellationToken.None));

        exception.Current.ShouldBe(RunStatus.Cancelled);
        exception.Decision.ShouldBe("cancel");
    }

    private static ISubjectScopeAccessor NewScopeAccessor()
    {
        var accessor = Substitute.For<ISubjectScopeAccessor>();
        _ = accessor.AsSystem(Arg.Any<string>()).Returns(static _ => new NoOpScope());
        return accessor;
    }

    private static async Task<OrchestrationDbContext> NewDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: $"runs-decisions-{Guid.NewGuid()}")
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

        _ = db.Runs.Add(run);
        _ = await db.SaveChangesAsync();
        return run;
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
