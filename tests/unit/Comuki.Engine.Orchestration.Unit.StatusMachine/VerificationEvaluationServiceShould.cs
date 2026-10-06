using Comuki.Engine.Orchestration.Application.Verification;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.Verification;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Engine.Orchestration.Infrastructure.Verification;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Contracts.Verification;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// Intent test for the verification evaluation path that
/// <c>WorkItemOwnedTransition.ApplyAsync</c> calls when a work item
/// reaches a terminal phase (add-orchestra §3 — Coda,
/// <c>verification/spec.md</c> Requirement "VerificationRecord is a
/// per-WorkItem sibling table"). The terminalization path inside
/// <c>WorkItemQueueEf</c> is a guarded
/// <c>UPDATE ... RETURNING run_id</c> raw-SQL branch that
/// <see cref="Microsoft.EntityFrameworkCore.InMemory"/> cannot
/// execute, so this test exercises the downstream
/// <see cref="VerificationEvaluationService.EvaluateAsync"/> call in
/// isolation — with <c>Enabled = true</c> on the options, the
/// service iterates the providers, calls
/// <see cref="IVerificationGateProvider.EvaluateAsync"/>, upserts the
/// record, and stamps the journal event. The real production path
/// commits the open transaction after this method returns; this
/// test pins the contract the transaction commits for — any change
/// that short-circuits the provider, the store, or the journal
/// surfaces here.
/// </summary>
public sealed class VerificationEvaluationServiceShould
{
    private static readonly DateTimeOffset now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given Enabled=true and a registered provider, when the terminal path calls EvaluateAsync, then the provider's EvaluateAsync runs, the record upserts, and the journal stamps the gate.evaluated event")]
    public async Task EvaluatesProviderAndStampsRecordAndJournalOnTerminalPathAsync()
    {
        var context = NewContext();
        var projectId = ProjectId.New();
        var run = Run.Create(projectId, now);
        var workItem = WorkItem.Create(
            run.Id,
            profileKey: "verify",
            image: "ghcr.io/comuki/worker@sha256:1",
            envClass: "net10-sdk-bun",
            profilesRef: "refs/heads/main",
            brief: "{}",
            initialStatus: WorkItemStatus.Queued,
            now);
        context.Runs.Add(run);
        context.WorkItems.Add(workItem);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var provider = Substitute.For<IVerificationGateProvider>();
        provider.GateName.Returns("verify:generic-command-run");
        provider.AppliesTo(Arg.Any<VerificationContext>()).Returns(true);
        provider.EvaluateAsync(Arg.Any<VerificationContext>(), Arg.Any<CancellationToken>())
            .Returns(new GateVerdictResult(
                GateVerdict.Passed,
                [new GateEvidenceRef(GateEvidenceKind.Cmdiff, new Uri("comuki://artifacts/c.diff"))],
                "verifier-7"));

        var registry = new VerificationProviderRegistry([provider]);
        var store = Substitute.For<IVerificationRecordStore>();
        var journal = Substitute.For<IRunJournal>();
        var projectSettings = Substitute.For<IProjectVerificationSettings>();
        projectSettings.IsVerificationEnabled(projectId).Returns(true);

        var options = Microsoft.Extensions.Options.Options.Create(new VerificationOptions { Enabled = true });

        var service = new VerificationEvaluationService(
            store,
            registry,
            context,
            journal,
            projectSettings,
            options,
            new VerificationFakeTimeProvider(now),
            NullLogger<VerificationEvaluationService>.Instance);

        await service.EvaluateAsync(workItem.Id, run.Id, TestContext.Current.CancellationToken);

        // Provider was called with the right context.
        await provider.Received(1).EvaluateAsync(
            Arg.Is<VerificationContext>(context =>
                context.WorkItemId == workItem.Id
                && context.RunId == run.Id
                && context.ProjectId == projectId
                && context.ProjectVerifyEnabled),
            TestContext.Current.CancellationToken);

        // Record was upserted once.
        await store.Received(1).UpsertAsync(
            Arg.Is<VerificationRecord>(record =>
                record.WorkItemId == workItem.Id
                && record.GateName == "verify:generic-command-run"
                && record.Verdict == GateVerdict.Passed
                && record.EvidenceRefs.Count == 1
                && record.EvidenceRefs[0].Kind == GateEvidenceKind.Cmdiff),
            TestContext.Current.CancellationToken);

        // Journal stamped the gate.evaluated event in the same scope
        // (the work-item terminalization transaction is open at this
        // point — the journal append enlists on the caller's
        // connection by sharing the scoped DbContext).
        await journal.Received(1).AppendAsync(
            Arg.Is<RunEventEntry>(entry =>
                entry.Type == "gate.evaluated"
                && entry.RunId == run.Id
                && entry.PayloadJson.Contains("\"verdict\":\"passed\"")
                && entry.PayloadJson.Contains("\"gateName\":\"verify:generic-command-run\"")),
            TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given Enabled=false, when the terminal path calls EvaluateAsync, then no provider runs, no record is upserted, and no journal event is stamped")]
    public async Task ShortCircuitsWhenOptionIsDisabledAsync()
    {
        var context = NewContext();
        var projectId = ProjectId.New();
        var run = Run.Create(projectId, now);
        var workItem = WorkItem.Create(
            run.Id,
            profileKey: "verify",
            image: "ghcr.io/comuki/worker@sha256:1",
            envClass: "net10-sdk-bun",
            profilesRef: "refs/heads/main",
            brief: "{}",
            initialStatus: WorkItemStatus.Queued,
            now);
        context.Runs.Add(run);
        context.WorkItems.Add(workItem);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var provider = Substitute.For<IVerificationGateProvider>();
        provider.GateName.Returns("verify:generic-command-run");
        provider.AppliesTo(Arg.Any<VerificationContext>()).Returns(true);

        var registry = new VerificationProviderRegistry([provider]);
        var store = Substitute.For<IVerificationRecordStore>();
        var journal = Substitute.For<IRunJournal>();
        var projectSettings = Substitute.For<IProjectVerificationSettings>();
        projectSettings.IsVerificationEnabled(projectId).Returns(true);

        var options = Microsoft.Extensions.Options.Options.Create(new VerificationOptions { Enabled = false });

        var service = new VerificationEvaluationService(
            store,
            registry,
            context,
            journal,
            projectSettings,
            options,
            new VerificationFakeTimeProvider(now),
            NullLogger<VerificationEvaluationService>.Instance);

        await service.EvaluateAsync(workItem.Id, run.Id, TestContext.Current.CancellationToken);

        await provider.DidNotReceiveWithAnyArgs().EvaluateAsync(
            Arg.Any<VerificationContext>(),
            Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().UpsertAsync(
            Arg.Any<VerificationRecord>(),
            Arg.Any<CancellationToken>());
        await journal.DidNotReceiveWithAnyArgs().AppendAsync(
            Arg.Any<RunEventEntry>(),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given Enabled=true but the project has verification disabled, when the terminal path calls EvaluateAsync, then the context carries ProjectVerifyEnabled=false through to AppliesTo")]
    public async Task SkipsWhenProjectGateIsOffAsync()
    {
        var context = NewContext();
        var projectId = ProjectId.New();
        var run = Run.Create(projectId, now);
        var workItem = WorkItem.Create(
            run.Id,
            profileKey: "verify",
            image: "ghcr.io/comuki/worker@sha256:1",
            envClass: "net10-sdk-bun",
            profilesRef: "refs/heads/main",
            brief: "{}",
            initialStatus: WorkItemStatus.Queued,
            now);
        context.Runs.Add(run);
        context.WorkItems.Add(workItem);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var provider = Substitute.For<IVerificationGateProvider>();
        provider.GateName.Returns("verify:generic-command-run");
        provider.AppliesTo(Arg.Any<VerificationContext>()).Returns(false);
        // Set up EvaluateAsync so a stray call returns a benign value
        // — the test asserts this method is NOT called, but a return
        // here keeps the substitute honest if a future service
        // refactor reorders the guards.
        provider.EvaluateAsync(Arg.Any<VerificationContext>(), Arg.Any<CancellationToken>())
            .Returns(new GateVerdictResult(GateVerdict.Pending, [], "verifier-7"));

        var registry = new VerificationProviderRegistry([provider]);
        var store = Substitute.For<IVerificationRecordStore>();
        var journal = Substitute.For<IRunJournal>();
        var projectSettings = Substitute.For<IProjectVerificationSettings>();
        projectSettings.IsVerificationEnabled(projectId).Returns(false);

        var options = Microsoft.Extensions.Options.Options.Create(new VerificationOptions { Enabled = true });

        var service = new VerificationEvaluationService(
            store,
            registry,
            context,
            journal,
            projectSettings,
            options,
            new VerificationFakeTimeProvider(now),
            NullLogger<VerificationEvaluationService>.Instance);

        await service.EvaluateAsync(workItem.Id, run.Id, TestContext.Current.CancellationToken);

        // Project's verification gate is off — the service consults
        // the provider (AppliesTo) with the off-state surfaced, the
        // provider returns false, the service short-circuits and
        // never runs EvaluateAsync / upserts / stamps the journal.
        provider.Received(1).AppliesTo(
            Arg.Is<VerificationContext>(static context => !context.ProjectVerifyEnabled));
        await provider.DidNotReceiveWithAnyArgs().EvaluateAsync(
            Arg.Any<VerificationContext>(),
            Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().UpsertAsync(
            Arg.Any<VerificationRecord>(),
            Arg.Any<CancellationToken>());
        await journal.DidNotReceiveWithAnyArgs().AppendAsync(
            Arg.Any<RunEventEntry>(),
            Arg.Any<CancellationToken>());
    }

    private static OrchestrationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: $"verification-{Guid.NewGuid():N}")
            .ConfigureWarnings(static warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new OrchestrationDbContext(options, scopeAccessor: null);
    }
}

/// <summary>Deterministic clock for the verification evaluation tests.</summary>
internal sealed class VerificationFakeTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private readonly DateTimeOffset utcNow = initial;

    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }
}
