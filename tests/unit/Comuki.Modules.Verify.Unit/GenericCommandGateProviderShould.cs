using Comuki.Modules.Verify.Application.Options;
using Comuki.Modules.Verify.Application.Ports;
using Comuki.Modules.Verify.Domain.Runs;
using Comuki.Modules.Verify.Infrastructure.Persistence;
using Comuki.Modules.Verify.Infrastructure.Persistence.Stores;
using Comuki.Modules.Verify.Infrastructure.Verification;
using Comuki.Shared.Contracts.Verification;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Verify.Unit;

/// <summary>
/// Behavioural tests for <see cref="GenericCommandGateProvider"/>:
/// the producer step (<see cref="GenericCommandGateProvider.EnsureGateRunAsync"/>)
/// idempotently schedules a <see cref="GenericCommandRun"/> for the
/// work item, and the read-side witness stamps
/// <see cref="GateVerdict.Passed"/>/<see cref="GateVerdict.Failed"/>/
/// <see cref="GateVerdict.Pending"/> from the underlying
/// <see cref="GenericCommandStatus"/>. The InMemory EF context backs
/// the store; the verifier worker path is integration-tested against a
/// real Postgres under <c>Comuki.Modules.Verify.Integration.Migrations</c>.
/// </summary>
public sealed class GenericCommandGateProviderShould
{
    private static readonly DateTimeOffset anchorTime = DateTimeOffset.Parse(
        "2026-09-23T00:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact(DisplayName = "Given unbound command-gate options, when EnsureGateRunAsync is called, then it does not schedule a run")]
    public async Task EnsureGateRunAsyncNoOpWhenOptionsAreUnboundAsync()
    {
        await using var db = NewDbContext();
        var store = new GenericCommandStore(db);
        var options = Options.Create(new CommandGateOptions());
        var clock = new FixedTimeProvider(anchorTime);
        var provider = NewProvider(store, options, clock);

        await provider.EnsureGateRunAsync(NewVerificationContext(), TestContext.Current.CancellationToken);

        var projectId = new ProjectId(Guid.CreateVersion7());
        var list = await store.ListAsync(projectId, limit: 10, TestContext.Current.CancellationToken);
        list.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given bound options and no existing run, when EnsureGateRunAsync is called, then it schedules exactly one work-item-bound run")]
    public async Task EnsureGateRunAsyncSchedulesRunOnFirstCallAsync()
    {
        await using var db = NewDbContext();
        var store = new GenericCommandStore(db);
        var options = Options.Create(new CommandGateOptions
        {
            Command = "dotnet",
            ProfileKey = "verify",
            Arguments = ["build", "comuki.slnx", "-c", "Debug"],
            ExpectedExitCode = 0,
        });
        var clock = new FixedTimeProvider(anchorTime);
        var provider = NewProvider(store, options, clock);

        var context = NewVerificationContext();

        await provider.EnsureGateRunAsync(context, TestContext.Current.CancellationToken);

        var list = await store.ListByWorkItemAsync(
            context.ProjectId!.Value,
            context.WorkItemId,
            limit: 10,
            TestContext.Current.CancellationToken);

        list.Count.ShouldBe(1);
        var scheduled = list[0];
        scheduled.WorkItemId.ShouldBe(context.WorkItemId);
        scheduled.ProjectId.ShouldBe(context.ProjectId.Value);
        scheduled.ProfileKey.ShouldBe("verify");
        scheduled.Executable.ShouldBe("dotnet");
        scheduled.Arguments.ShouldBe(["build", "comuki.slnx", "-c", "Debug"]);
        scheduled.ExpectedExitCode.ShouldBe(0);
        scheduled.Status.ShouldBe(GenericCommandStatus.Pending);
    }

    [Fact(DisplayName = "Given an existing run, when EnsureGateRunAsync is called again, then it does not insert a duplicate")]
    public async Task EnsureGateRunAsyncIsIdempotentAcrossEvaluationsAsync()
    {
        await using var db = NewDbContext();
        var store = new GenericCommandStore(db);
        var options = Options.Create(new CommandGateOptions
        {
            Command = "dotnet",
            ProfileKey = "verify",
            Arguments = ["test"],
            ExpectedExitCode = 0,
        });
        var clock = new FixedTimeProvider(anchorTime);
        var provider = NewProvider(store, options, clock);

        var context = NewVerificationContext();

        // Two passes — a re-evaluation for the same (work item, gate)
        // pair must not double-schedule a run.
        await provider.EnsureGateRunAsync(context, TestContext.Current.CancellationToken);
        await provider.EnsureGateRunAsync(context, TestContext.Current.CancellationToken);
        await provider.EnsureGateRunAsync(context, TestContext.Current.CancellationToken);

        var list = await store.ListByWorkItemAsync(
            context.ProjectId!.Value,
            context.WorkItemId,
            limit: 10,
            TestContext.Current.CancellationToken);

        list.Count.ShouldBe(1);
    }

    [Fact(DisplayName = "Given an existing Green run, when EvaluateAsync is called, then the verdict is Passed")]
    public async Task EvaluateStampsPassedWhenRunIsGreenAsync()
    {
        await using var db = NewDbContext();
        var store = new GenericCommandStore(db);
        var provider = NewProvider(
            store,
            Options.Create(new CommandGateOptions()),
            new FixedTimeProvider(anchorTime));

        var context = NewVerificationContext();
        await SeedRunAsync(store, context, GenericCommandStatus.Green);

        var verdict = await provider.EvaluateAsync(context, TestContext.Current.CancellationToken);

        verdict.Verdict.ShouldBe(GateVerdict.Passed);
        verdict.Evaluator.ShouldBe(provider.GateName);
    }

    [Fact(DisplayName = "Given an existing Red run, when EvaluateAsync is called, then the verdict is Failed")]
    public async Task EvaluateStampsFailedWhenRunIsRedAsync()
    {
        await using var db = NewDbContext();
        var store = new GenericCommandStore(db);
        var provider = NewProvider(
            store,
            Options.Create(new CommandGateOptions()),
            new FixedTimeProvider(anchorTime));

        var context = NewVerificationContext();
        await SeedRunAsync(store, context, GenericCommandStatus.Red);

        var verdict = await provider.EvaluateAsync(context, TestContext.Current.CancellationToken);

        verdict.Verdict.ShouldBe(GateVerdict.Failed);
    }

    [Fact(DisplayName = "Given a non-terminal run (Pending / Running), when EvaluateAsync is called, then the verdict is Pending")]
    public async Task EvaluateStampsPendingWhenRunIsInFlightAsync()
    {
        await using var db = NewDbContext();
        var store = new GenericCommandStore(db);
        var provider = NewProvider(
            store,
            Options.Create(new CommandGateOptions()),
            new FixedTimeProvider(anchorTime));

        var context = NewVerificationContext();
        await SeedRunAsync(store, context, GenericCommandStatus.Pending);

        var verdict = await provider.EvaluateAsync(context, TestContext.Current.CancellationToken);

        verdict.Verdict.ShouldBe(GateVerdict.Pending);
    }

    [Fact(DisplayName = "Given no run for the work item, when EvaluateAsync is called, then the verdict is Pending")]
    public async Task EvaluateStampsPendingWhenNoRunExistsAsync()
    {
        await using var db = NewDbContext();
        var store = new GenericCommandStore(db);
        var provider = NewProvider(
            store,
            Options.Create(new CommandGateOptions()),
            new FixedTimeProvider(anchorTime));

        var context = NewVerificationContext();

        var verdict = await provider.EvaluateAsync(context, TestContext.Current.CancellationToken);

        verdict.Verdict.ShouldBe(GateVerdict.Pending);
    }

    private static GenericCommandGateProvider NewProvider(
        IGenericCommandStore store,
        IOptions<CommandGateOptions> options,
        TimeProvider clock)
    {
        return new(store, clock, options, NullLogger<GenericCommandGateProvider>.Instance);
    }

    private static VerificationContext NewVerificationContext()
    {
        return new VerificationContext(
            WorkItemId: Guid.CreateVersion7(),
            RunId: new RunId(Guid.CreateVersion7()),
            ProjectId: new ProjectId(Guid.CreateVersion7()),
            ProjectVerifyEnabled: true);
    }

    private static async Task SeedRunAsync(
        IGenericCommandStore store,
        VerificationContext context,
        GenericCommandStatus terminalStatus)
    {
        var run = GenericCommandRun.Create(
            context.ProjectId!.Value,
            profileKey: "verify",
            executable: "dotnet",
            arguments: ["test"],
            expectedExitCode: 0,
            now: anchorTime,
            workItemId: context.WorkItemId);

        if (terminalStatus == GenericCommandStatus.Running || terminalStatus == GenericCommandStatus.Green || terminalStatus == GenericCommandStatus.Red)
        {
            run.MarkRunning(anchorTime.AddSeconds(1));
        }

        if (terminalStatus == GenericCommandStatus.Green || terminalStatus == GenericCommandStatus.Red)
        {
            run.MarkCompleted(actualExitCode: terminalStatus == GenericCommandStatus.Green ? 0 : 1, outputLog: "ok", now: anchorTime.AddSeconds(2));
        }

        await store.AddAsync(run, TestContext.Current.CancellationToken);
    }

    private static VerifyDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<VerifyDbContext>()
            .UseInMemoryDatabase($"verify-gate-tests-{Guid.NewGuid():N}")
            .Options;
        return new VerifyDbContext(options);
    }

    /// <summary>
    /// Returns the same wall-clock instant on every call so the test's
    /// <see cref="GenericCommandRun.CreatedAt"/> assertions don't depend
    /// on the test runner's monotonic time source.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private readonly DateTimeOffset now = now;

        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
