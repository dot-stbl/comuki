using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Runs;
using Comuki.Shared.Kernel.Harness;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Steer;

/// <summary>
/// <see cref="RunHarnessResolver"/>: the default resolver that
/// joins runs to work_items and looks the live harness up by name
/// (add-orchestra Phase 1c, B2). Two contracts the steering
/// endpoint rides on:
/// <list type="number">
///   <item>Profile key matches a registered harness name
///   (<c>test-fake-pi</c> → <see cref="HarnessIds.TestFakePi"/>) —
///   the test-fake-pi surface the unit tests assert against.</item>
///   <item>Profile key has no registered harness (the production
///   case: a profile like <c>implement</c> without
///   <c>harness:</c> frontmatter) — the resolver falls back to
///   <see cref="HarnessIds.Pi"/>, the canonical prod harness
///   the host registers. Without this fallback, every prod
///   profile lands on the no-harness path and steering is
///   impossible.</item>
/// </list>
/// </summary>
public sealed class RunHarnessResolverShould
{
    [Fact(DisplayName = "Given a profile with no matching harness, when ResolveAsync runs, then the Pi default is returned (LiveSession=true)")]
    public async Task FallsBackToPiWhenProfileKeyIsNotRegisteredAsync()
    {
        var (resolver, _, runId) = await NewResolverWithRunningWorkItemAsync(profileKey: "implement");

        var resolved = await resolver.ResolveAsync(
            runId: runId,
            cancellationToken: TestContext.Current.CancellationToken);

        resolved.ShouldNotBeNull();
        resolved!.Name.ShouldBe(HarnessIds.Pi);
        resolved.Capabilities.LiveSession.ShouldBeTrue(
            "the Pi default is the live-session harness — the prod steering path is the bidi TurnInput branch");
    }

    [Fact(DisplayName = "Given a profile with an explicit test-fake-pi key, when ResolveAsync runs, then the test-fake-pi harness is returned (not the Pi fallback)")]
    public async Task ExplicitTestFakePiNameWinsOverThePiFallbackAsync()
    {
        var (resolver, _, runId) = await NewResolverWithRunningWorkItemAsync(profileKey: HarnessIds.TestFakePi);

        var resolved = await resolver.ResolveAsync(
            runId: runId,
            cancellationToken: TestContext.Current.CancellationToken);

        resolved.ShouldNotBeNull();
        resolved!.Name.ShouldBe(HarnessIds.TestFakePi);
    }

    [Fact(DisplayName = "Given a run with no Running work item, when ResolveAsync runs, then null is returned")]
    public async Task ReturnsNullWhenNoRunningWorkItemAsync()
    {
        var (resolver, _, runId) = await NewResolverWithRunningWorkItemAsync(profileKey: null);

        var resolved = await resolver.ResolveAsync(
            runId: runId,
            cancellationToken: TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    /// <summary>
    /// Builds a fresh in-memory <see cref="OrchestrationDbContext"/>,
    /// seeds one Running work item with the given <paramref name="profileKey"/>
    /// (or none when <c>null</c>), and constructs a
    /// <see cref="HarnessRegistry"/> with the production Pi and
    /// test-fake-pi harnesses — the two registrations the host
    /// composition always makes.
    /// </summary>
    /// <param name="profileKey">Profile key the work item carries
    /// (e.g. <c>"implement"</c>, <c>"test-fake-pi"</c>); <c>null</c>
    /// means the test seeds no Running work item at all.</param>
    private static async Task<(RunHarnessResolver Resolver, OrchestrationDbContext Db, RunId RunId)> NewResolverWithRunningWorkItemAsync(string? profileKey)
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: $"run-harness-resolver-{Guid.NewGuid():N}")
            .Options;
        var db = new OrchestrationDbContext(options, scopeAccessor: null);

        RunId runId = default;
        if (profileKey is not null)
        {
            var run = await SeedRunAsync(db);
            runId = run.Id;
            var now = DateTimeOffset.UtcNow;
            var item = WorkItem.Create(
                run.Id,
                profileKey,
                image: "image:placeholder",
                envClass: "test",
                profilesRef: "main",
                brief: /*lang=json,strict*/ "{\"goal\":\"probe\"}",
                WorkItemStatus.Queued,
                now);
            item.AssignLease(
                workerId: WorkerId.New(),
                generation: 1,
                leaseUntil: now.AddMinutes(5),
                now);
            db.WorkItems.Add(item);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var registry = new HarnessRegistry(
            [
                new PiHarnessCapability(),
                new TestFakeHarnessCapability(liveSession: false),
            ]);
        return (new RunHarnessResolver(db, registry), db, runId);
    }

    private static async Task<Run> SeedRunAsync(OrchestrationDbContext db)
    {
        var run = Run.Create(
            projectId: ProjectId.New(),
            now: DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return run;
    }
}
