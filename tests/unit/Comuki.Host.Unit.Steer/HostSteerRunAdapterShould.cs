using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Runs;
using Comuki.Host.Workers.Grpc;
using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Shared.Bootstrap.Versioning;
using Comuki.Shared.Contracts.Grpc;
using Comuki.Shared.Kernel.Harness;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Steer;

/// <summary>
/// <see cref="HostSteerRunAdapter"/>: the operator-initiated steer
/// (add-orchestra §1 — Baton). The InMemory provider exercises both
/// the no-LiveSession follow-up staging path (Phase 1a canonical)
/// and the LiveSession bidi <see cref="TurnInput"/> path (Phase
/// 1c). The terminal-run refusal, the unknown-run path, the
/// no-live-lease resilience, the LiveSession=true successful
/// delivery, and the LiveSession=true miss (worker has no live
/// stream) are the load-bearing branches — each gets a test.
/// </summary>
public sealed class HostSteerRunAdapterShould
{
    [Fact(DisplayName = "Given a Queued run, when SteerAsync is called, then a Queued follow-up WorkItem is staged with the operator's text as the brief")]
    public async Task SteerQueuedRunStagesFollowUpAsync()
    {
        var db = NewDb();
        var resolver = NewResolverStub(liveWorker: null);
        var harnessResolver = NewHarnessResolverStub(liveHarness: null);
        var commandPipe = NewCommandPipeStub(delivered: true);
        var projects = NewProjectsStub();
        var adapter = NewAdapter(db, resolver, harnessResolver, commandPipe, projects);
        var runId = RunId.New();
        await SeedRunAsync(db, runId, status: RunStatus.Queued);
        const string steerText = "redirect to a different approach";

        var result = await adapter.SteerAsync(runId, steerText, TestContext.Current.CancellationToken);

        result.Delivered.ShouldBeTrue();
        result.FollowUpWorkItemId.ShouldNotBeNull();

        var followUp = await db.WorkItems.SingleAsync(item => item.Id == result.FollowUpWorkItemId!.Value, TestContext.Current.CancellationToken);
        followUp.RunId.ShouldBe(runId);
        followUp.Status.ShouldBe(WorkItemStatus.Queued);
        followUp.Brief.ShouldContain(steerText);
    }

    [Fact(DisplayName = "Given a Running run with a live worker, when SteerAsync is called, then the follow-up is staged regardless of the live worker's bidi availability (Phase 1a canonical path)")]
    public async Task SteerRunningRunStagesFollowUpEvenWithLiveWorkerAsync()
    {
        var db = NewDb();
        var liveWorker = WorkerId.New();
        var resolver = NewResolverStub(liveWorker);
        var harnessResolver = NewHarnessResolverStub(liveHarness: null);
        var commandPipe = NewCommandPipeStub(delivered: true);
        var projects = NewProjectsStub();
        var adapter = NewAdapter(db, resolver, harnessResolver, commandPipe, projects);
        var runId = RunId.New();
        await SeedRunAsync(db, runId, status: RunStatus.Running);

        var result = await adapter.SteerAsync(runId, "course-correct", TestContext.Current.CancellationToken);

        result.Delivered.ShouldBeTrue();
        result.FollowUpWorkItemId.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given a terminal Succeeded run, when SteerAsync is called, then RunNotRunningForSteerException is thrown and no follow-up is staged")]
    public async Task SteerTerminalRunRefusesAndStagesNothingAsync()
    {
        var db = NewDb();
        var resolver = NewResolverStub(liveWorker: null);
        var harnessResolver = NewHarnessResolverStub(liveHarness: null);
        var commandPipe = NewCommandPipeStub(delivered: true);
        var projects = NewProjectsStub();
        var adapter = NewAdapter(db, resolver, harnessResolver, commandPipe, projects);
        var runId = RunId.New();
        await SeedRunAsync(db, runId, status: RunStatus.Succeeded);

        var exception = await Should.ThrowAsync<RunNotRunningForSteerException>(
            () => adapter.SteerAsync(runId, "anything", TestContext.Current.CancellationToken));

        exception.Current.ShouldBe(RunStatus.Succeeded);
        (await db.WorkItems.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact(DisplayName = "Given a Failed run, when SteerAsync is called, then RunNotRunningForSteerException is thrown")]
    public async Task SteerFailedRunRefusesAsync()
    {
        var db = NewDb();
        var resolver = NewResolverStub(liveWorker: null);
        var harnessResolver = NewHarnessResolverStub(liveHarness: null);
        var commandPipe = NewCommandPipeStub(delivered: true);
        var projects = NewProjectsStub();
        var adapter = NewAdapter(db, resolver, harnessResolver, commandPipe, projects);
        var runId = RunId.New();
        await SeedRunAsync(db, runId, status: RunStatus.Failed);

        await Should.ThrowAsync<RunNotRunningForSteerException>(
            () => adapter.SteerAsync(runId, "anything", TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given a Cancelled run, when SteerAsync is called, then RunNotRunningForSteerException is thrown")]
    public async Task SteerCancelledRunRefusesAsync()
    {
        var db = NewDb();
        var resolver = NewResolverStub(liveWorker: null);
        var harnessResolver = NewHarnessResolverStub(liveHarness: null);
        var commandPipe = NewCommandPipeStub(delivered: true);
        var projects = NewProjectsStub();
        var adapter = NewAdapter(db, resolver, harnessResolver, commandPipe, projects);
        var runId = RunId.New();
        await SeedRunAsync(db, runId, status: RunStatus.Cancelled);

        await Should.ThrowAsync<RunNotRunningForSteerException>(
            () => adapter.SteerAsync(runId, "anything", TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given a successful steer, when the follow-up is staged, then a run_events row of type run.steer_followup_queued is appended with the steer text and the follow-up id")]
    public async Task SteerAppendsRunEventAsync()
    {
        var db = NewDb();
        var resolver = NewResolverStub(liveWorker: null);
        var harnessResolver = NewHarnessResolverStub(liveHarness: null);
        var commandPipe = NewCommandPipeStub(delivered: true);
        var projects = NewProjectsStub();
        var adapter = NewAdapter(db, resolver, harnessResolver, commandPipe, projects);
        var runId = RunId.New();
        await SeedRunAsync(db, runId, status: RunStatus.Running);
        const string steerText = "the dashboard will see this in the timeline";

        var result = await adapter.SteerAsync(runId, steerText, TestContext.Current.CancellationToken);

        var runEvent = await db.RunEvents.SingleAsync(evt => evt.RunId == runId, TestContext.Current.CancellationToken);
        runEvent.Type.ShouldBe("run.steer_followup_queued");
        runEvent.Payload.ShouldContain(steerText);
        runEvent.Payload.ShouldContain(result.FollowUpWorkItemId!.Value.ToString());
    }

    [Fact(DisplayName = "Given a run, when SteerAsync is called, then the follow-up is staged as Queued and not Blocked (it can be claimed independently of the in-flight item)")]
    public async Task FollowUpIsNotBlockedOnRunningItemAsync()
    {
        var db = NewDb();
        var liveWorker = WorkerId.New();
        var resolver = NewResolverStub(liveWorker);
        var harnessResolver = NewHarnessResolverStub(liveHarness: null);
        var commandPipe = NewCommandPipeStub(delivered: true);
        var projects = NewProjectsStub();
        var adapter = NewAdapter(db, resolver, harnessResolver, commandPipe, projects);
        var runId = RunId.New();
        await SeedRunAsync(db, runId, status: RunStatus.Running);
        // The in-flight item still exists alongside the follow-up.
        await SeedItemAsync(db, runId, "implement", WorkItemStatus.Running, liveWorker, leased: true);

        var result = await adapter.SteerAsync(runId, "follow-up please", TestContext.Current.CancellationToken);

        var followUp = await db.WorkItems.SingleAsync(item => item.Id == result.FollowUpWorkItemId!.Value, TestContext.Current.CancellationToken);
        followUp.Status.ShouldBe(WorkItemStatus.Queued);
        // No dependency row should link the follow-up to the in-flight item
        // (the follow-up is independent in the DAG — a fresh worker
        // claims it as soon as the in-flight lease is fenced or reaped).
        (await db.WorkItemDependencies.CountAsync(dep => dep.WorkItemId == followUp.Id, TestContext.Current.CancellationToken))
            .ShouldBe(0);
    }

    [Fact(DisplayName = "Given a run, when SteerAsync declares the system scope \\\"runs-steer\\\", the read is unrestricted (mirrors the run-cancel adapter's scope contract)")]
    public async Task SteerDeclaresSystemScopeAsync()
    {
        var db = NewDb();
        var scopeAccessor = Substitute.For<ISubjectScopeAccessor>();
        var resolver = NewResolverStub(liveWorker: null);
        var harnessResolver = NewHarnessResolverStub(liveHarness: null);
        var commandPipe = NewCommandPipeStub(delivered: true);
        var projects = NewProjectsStub();
        var adapter = new HostSteerRunAdapter(
            db, scopeAccessor, resolver, harnessResolver, commandPipe,
            Options.Create(new SteeringWorkerDefaults()),
            ComukiBuildInformation.Unknown, TimeProvider.System, projects,
            NullLogger<HostSteerRunAdapter>.Instance);
        var runId = RunId.New();
        await SeedRunAsync(db, runId, status: RunStatus.Queued);

        await adapter.SteerAsync(runId, "x", TestContext.Current.CancellationToken);

        scopeAccessor.Received().AsSystem("runs-steer");
    }

    [Fact(DisplayName = "Given a Running run with a live worker and a LiveSession harness, when SteerAsync is called, then the bidi TurnInput path delivers and returns delivered:true with no follow-up")]
    public async Task SteerLiveSessionDeliversViaTurnInputAsync()
    {
        var db = NewDb();
        var liveWorker = WorkerId.New();
        var liveHarness = new InProcessHarness(LiveSession: true);
        var resolver = NewResolverStub(liveWorker);
        var harnessResolver = NewHarnessResolverStub(liveHarness);
        var commandPipe = NewCommandPipeStub(delivered: true);
        var projects = NewProjectsStub();
        var adapter = NewAdapter(db, resolver, harnessResolver, commandPipe, projects);
        var runId = RunId.New();
        await SeedRunAsync(db, runId, status: RunStatus.Running);
        const string steerText = "redirect to the live session";

        var result = await adapter.SteerAsync(runId, steerText, TestContext.Current.CancellationToken);

        result.Delivered.ShouldBeTrue();
        result.FollowUpWorkItemId.ShouldBeNull();
        // LiveSession path: NO follow-up work item is staged.
        (await db.WorkItems.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        // LiveSession path: NO run_events row of the follow-up kind.
        (await db.RunEvents.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact(DisplayName = "Given a Running run with a live worker and a LiveSession harness, but no live bidi stream, when SteerAsync is called, then the result is delivered:false with no follow-up")]
    public async Task SteerLiveSessionMissesWhenNoLiveStreamAsync()
    {
        var db = NewDb();
        var liveWorker = WorkerId.New();
        var liveHarness = new InProcessHarness(LiveSession: true);
        var resolver = NewResolverStub(liveWorker);
        var harnessResolver = NewHarnessResolverStub(liveHarness);
        // Hub returns false — the worker has no live stream
        // (reaper reclaimed the lease between mint and steer).
        var commandPipe = NewCommandPipeStub(delivered: false);
        var projects = NewProjectsStub();
        var adapter = NewAdapter(db, resolver, harnessResolver, commandPipe, projects);
        var runId = RunId.New();
        await SeedRunAsync(db, runId, status: RunStatus.Running);

        var result = await adapter.SteerAsync(runId, "any text", TestContext.Current.CancellationToken);

        result.Delivered.ShouldBeFalse();
        result.FollowUpWorkItemId.ShouldBeNull();
        // Miss path: NO follow-up — the spec marks the call
        // non-fatal; the caller may retry. The follow-up is the
        // no-LiveSession path's job, not the LiveSession miss.
        (await db.WorkItems.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact(DisplayName = "Given a Running run with a live worker but no live stream, when SteerAsync is called and the harness declares LiveSession=true, then the response is delivered:false (the harness resolver does not fall back to the follow-up path)")]
    public async Task SteerLiveSessionNoLiveWorkerReturnsDeliveredFalseAsync()
    {
        var db = NewDb();
        var liveHarness = new InProcessHarness(LiveSession: true);
        var resolver = NewResolverStub(liveWorker: null);
        var harnessResolver = NewHarnessResolverStub(liveHarness);
        var commandPipe = NewCommandPipeStub(delivered: true);
        var projects = NewProjectsStub();
        var adapter = NewAdapter(db, resolver, harnessResolver, commandPipe, projects);
        var runId = RunId.New();
        await SeedRunAsync(db, runId, status: RunStatus.Running);

        var result = await adapter.SteerAsync(runId, "any text", TestContext.Current.CancellationToken);

        result.Delivered.ShouldBeFalse();
        result.FollowUpWorkItemId.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a Running run with a no-LiveSession harness, when SteerAsync is called, then the follow-up is staged (Phase 1a canonical path)")]
    public async Task SteerNoLiveSessionStagesFollowUpAsync()
    {
        var db = NewDb();
        var liveWorker = WorkerId.New();
        var noLiveSessionHarness = new InProcessHarness(LiveSession: false);
        var resolver = NewResolverStub(liveWorker);
        var harnessResolver = NewHarnessResolverStub(noLiveSessionHarness);
        var commandPipe = NewCommandPipeStub(delivered: true);
        var projects = NewProjectsStub();
        var adapter = NewAdapter(db, resolver, harnessResolver, commandPipe, projects);
        var runId = RunId.New();
        await SeedRunAsync(db, runId, status: RunStatus.Running);

        var result = await adapter.SteerAsync(runId, "course-correct", TestContext.Current.CancellationToken);

        result.Delivered.ShouldBeTrue();
        result.FollowUpWorkItemId.ShouldNotBeNull();
        // No-LiveSession path: the bidi channel is NOT consulted.
        // TrySendTurnInput returns sync (bool); NSubstitute's
        // DidNotReceive().<method>() returns bool synchronously —
        // no await.
        commandPipe.DidNotReceive().TrySendTurnInput(
            Arg.Any<WorkerId>(),
            Arg.Any<TurnInput>());
    }

    [Fact(DisplayName = "Given a TurnInput command sent to the bidi channel, when the worker is registered, then the same Text/Role/Metadata are observed end-to-end")]
    public async Task TurnInputCarriesOperatorTextToBidiChannelAsync()
    {
        var db = NewDb();
        var liveWorker = WorkerId.New();
        var liveHarness = new InProcessHarness(LiveSession: true);
        var resolver = NewResolverStub(liveWorker);
        var harnessResolver = NewHarnessResolverStub(liveHarness);
        var commandPipe = Substitute.For<IWorkerCommandPipe>();
        commandPipe.TrySendTurnInput(Arg.Any<WorkerId>(), Arg.Any<TurnInput>())
            .Returns(true);
        var projects = NewProjectsStub();
        var adapter = NewAdapter(db, resolver, harnessResolver, commandPipe, projects);
        var runId = RunId.New();
        await SeedRunAsync(db, runId, status: RunStatus.Running);
        const string steerText = "the operator sees this on the bidi stream";

        await adapter.SteerAsync(runId, steerText, TestContext.Current.CancellationToken);

        // TrySendTurnInput returns sync (bool); NSubstitute's
        // Received(1).<method>() returns bool synchronously — no
        // await.
        commandPipe.Received(1).TrySendTurnInput(
            Arg.Is<WorkerId>(worker => worker == liveWorker),
            Arg.Is<TurnInput>(turn => turn.Text == steerText && turn.Role == "user"));
    }

    private static OrchestrationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: $"steer-{Guid.NewGuid():N}")
            .Options;
        return new OrchestrationDbContext(options, scopeAccessor: null);
    }

    private static HostSteerRunAdapter NewAdapter(
        OrchestrationDbContext db,
        IExecutionIdResolver resolver,
        IRunHarnessResolver harnessResolver,
        IWorkerCommandPipe commandPipe,
        IProjectStore projects)
    {
        return new(
            db,
            Substitute.For<ISubjectScopeAccessor>(),
            resolver,
            harnessResolver,
            commandPipe,
            Options.Create(new SteeringWorkerDefaults()),
            ComukiBuildInformation.Unknown,
            TimeProvider.System,
            projects,
            NullLogger<HostSteerRunAdapter>.Instance);
    }

    private static IExecutionIdResolver NewResolverStub(WorkerId? liveWorker)
    {
        var resolver = Substitute.For<IExecutionIdResolver>();
        resolver.ResolveAsync(Arg.Any<RunId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(liveWorker));
        return resolver;
    }

    private static IRunHarnessResolver NewHarnessResolverStub(IHarness? liveHarness)
    {
        var harnessResolver = Substitute.For<IRunHarnessResolver>();
        harnessResolver.ResolveAsync(Arg.Any<RunId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(liveHarness));
        return harnessResolver;
    }

    private static IWorkerCommandPipe NewCommandPipeStub(bool delivered)
    {
        var commandPipe = Substitute.For<IWorkerCommandPipe>();
        commandPipe.TrySendTurnInput(Arg.Any<WorkerId>(), Arg.Any<TurnInput>())
            .Returns(delivered);
        return commandPipe;
    }

    private static IProjectStore NewProjectsStub()
    {
        return ProjectStoreStub();
    }

    /// <summary>
    /// In-process <see cref="IHarness"/> for the unit test surface.
    /// Mirrors the production <c>TestFakeHarness</c> in
    /// <c>Comuki.Host.Translator.Runtime</c> without taking a
    /// Translator project reference from the Host unit test project —
    /// the test fake is a tiny surface (Name + Capabilities) and the
    /// harness contract is what the Host's <see cref="HarnessRegistry"/>
    /// ultimately holds.
    /// </summary>
    private sealed class InProcessHarness(bool LiveSession) : IHarness
    {
        public string Name => "in-process-harness";

        public HarnessCapabilities Capabilities { get; } = new(liveSession: LiveSession);
    }

    /// <summary>
    /// Per-test <see cref="IProjectStore"/> stub: returns a fresh
    /// <see cref="Project"/> on every <c>FindByIdAsync</c> with a
    /// non-null <c>EnvClass</c> — the env class the follow-up
    /// <see cref="WorkItem.Create"/> factory demands (an empty class
    /// makes the item permanently unclaimable, see
    /// <see cref="WorkItem.Create"/>'s env-class invariant).
    /// </summary>
    private static IProjectStore ProjectStoreStub()
    {
        var stub = Substitute.For<IProjectStore>();
        stub.FindByIdAsync(Arg.Any<ProjectId>(), Arg.Any<CancellationToken>())
            .Returns(static callInfo => Project.Create(
                "Test project",
                "test-project",
                null,
                null,
                null,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                envClass: "net10-sdk-bun"));
        return stub;
    }

    private static async Task SeedRunAsync(
        OrchestrationDbContext db,
        RunId runId,
        RunStatus status)
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var run = Run.Create(ProjectId.New(), now);
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
