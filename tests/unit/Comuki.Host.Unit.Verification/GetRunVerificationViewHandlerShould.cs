using Comuki.Engine.Orchestration.Application.Verification;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.Verification;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Runs.Verification;
using Comuki.Modules.Artifacts.Application.VisualArtifacts.Ports;
using Comuki.Modules.Artifacts.Domain.VisualArtifacts;
using Comuki.Shared.Contracts.Verification;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Verification;

/// <summary>
/// Behavioural tests for <see cref="GetRunVerificationViewHandler"/>:
/// the 404-by-design on missing runs, the empty-gates list for a run
/// that has no work items, the per-(work item, gate) flattening with
/// the lower-case verdict mapping to the wire string, and the
/// <c>cmdiff</c> bundle-member enrichment. The tests run against an
/// InMemory EF context so the host unit suite does not need a real
/// Postgres; the visual-artifact store is stubbed.
/// </summary>
public sealed class GetRunVerificationViewHandlerShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a run that does not exist, when GetAsync is called, then it returns null")]
    public async Task ReturnNullForUnknownRunAsync()
    {
        var db = NewDbContext();
        var records = Substitute.For<IVerificationRecordStore>();
        var artifacts = Substitute.For<IVisualArtifactStore>();
        var handler = new GetRunVerificationViewHandler(db, records, artifacts);

        var view = await handler.GetAsync(new RunId(Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        view.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a run with no work items, when GetAsync is called, then it returns an empty gates list and both booleans are false")]
    public async Task ReturnEmptyGatesForRunWithoutWorkItemsAsync()
    {
        var db = NewDbContext();
        var records = Substitute.For<IVerificationRecordStore>();
        var artifacts = Substitute.For<IVisualArtifactStore>();
        var projectId = ProjectId.New();
        var run = Run.Create(projectId, now);
        await db.Runs.AddAsync(run, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var handler = new GetRunVerificationViewHandler(db, records, artifacts);

        var view = await handler.GetAsync(run.Id, TestContext.Current.CancellationToken);

        view.ShouldNotBeNull();
        view!.RunId.ShouldBe(run.Id.Value);
        view.Gates.ShouldBeEmpty();
        view.Verified.ShouldBeFalse();
        view.VerificationPending.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a run with verdicts, when GetAsync is called, then it flattens them to per-gate views and surfaces both booleans")]
    public async Task FlattenVerdictsToGateViewsAsync()
    {
        var db = NewDbContext();
        var records = Substitute.For<IVerificationRecordStore>();
        var artifacts = Substitute.For<IVisualArtifactStore>();
        var projectId = ProjectId.New();
        var run = Run.Create(projectId, now);
        await db.Runs.AddAsync(run, TestContext.Current.CancellationToken);

        var workItemA = WorkItem.Create(
            run.Id,
            profileKey: "verify",
            image: "ghcr.io/comuki/worker@sha256:1",
            envClass: "net10-sdk-bun",
            profilesRef: "refs/heads/main",
            brief: "{}",
            initialStatus: WorkItemStatus.Queued,
            now);
        var workItemB = WorkItem.Create(
            run.Id,
            profileKey: "verify",
            image: "ghcr.io/comuki/worker@sha256:1",
            envClass: "net10-sdk-bun",
            profilesRef: "refs/heads/main",
            brief: "{}",
            initialStatus: WorkItemStatus.Queued,
            now);
        await db.WorkItems.AddAsync(workItemA, TestContext.Current.CancellationToken);
        await db.WorkItems.AddAsync(workItemB, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var evidence = new GateEvidenceRef[]
        {
            new(GateEvidenceKind.Stdout, new Uri("comuki://artifacts/a")),
        };
        records.ListByWorkItemsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                VerificationRecord.FromEvaluation(
                    workItemA.Id,
                    "verify:generic-command-run",
                    new GateVerdictResult(GateVerdict.Passed, evidence, "verifier-7"),
                    now),
                VerificationRecord.FromEvaluation(
                    workItemB.Id,
                    "verify:generic-command-run",
                    new GateVerdictResult(GateVerdict.Failed, [], "verifier-7"),
                    now),
            ]);
        artifacts.ListByWorkItemsAndFilenameAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns([]);
        var handler = new GetRunVerificationViewHandler(db, records, artifacts);

        var view = await handler.GetAsync(run.Id, TestContext.Current.CancellationToken);

        view.ShouldNotBeNull();
        view!.RunId.ShouldBe(run.Id.Value);
        view.Gates.Count.ShouldBe(2);
        view.Gates.ShouldContain(g => g.WorkItemId == workItemA.Id && g.Verdict == "passed");
        view.Gates.ShouldContain(g => g.WorkItemId == workItemB.Id && g.Verdict == "failed");
        // One passed + one failed: not all-passed (failed exists), no
        // pending → verified=false, verificationPending=false. The
        // all-passed and verification-pending scenarios live in their
        // own tests below.
        view.Verified.ShouldBeFalse();
        view.VerificationPending.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a run whose every gate is passed, when GetAsync is called, then Verified=true and Pending=false")]
    public async Task VerifiedWhenAllGatesPassAsync()
    {
        var db = NewDbContext();
        var records = Substitute.For<IVerificationRecordStore>();
        var artifacts = Substitute.For<IVisualArtifactStore>();
        var projectId = ProjectId.New();
        var run = Run.Create(projectId, now);
        await db.Runs.AddAsync(run, TestContext.Current.CancellationToken);

        var workItemA = WorkItem.Create(
            run.Id,
            profileKey: "verify",
            image: "ghcr.io/comuki/worker@sha256:1",
            envClass: "net10-sdk-bun",
            profilesRef: "refs/heads/main",
            brief: "{}",
            initialStatus: WorkItemStatus.Queued,
            now);
        var workItemB = WorkItem.Create(
            run.Id,
            profileKey: "verify",
            image: "ghcr.io/comuki/worker@sha256:1",
            envClass: "net10-sdk-bun",
            profilesRef: "refs/heads/main",
            brief: "{}",
            initialStatus: WorkItemStatus.Queued,
            now);
        await db.WorkItems.AddAsync(workItemA, TestContext.Current.CancellationToken);
        await db.WorkItems.AddAsync(workItemB, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        records.ListByWorkItemsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                VerificationRecord.FromEvaluation(
                    workItemA.Id,
                    "verify:generic-command-run",
                    new GateVerdictResult(GateVerdict.Passed, [], "verifier-7"),
                    now),
                VerificationRecord.FromEvaluation(
                    workItemB.Id,
                    "verify:generic-command-run",
                    new GateVerdictResult(GateVerdict.Passed, [], "verifier-7"),
                    now),
            ]);
        artifacts.ListByWorkItemsAndFilenameAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns([]);
        var handler = new GetRunVerificationViewHandler(db, records, artifacts);

        var view = await handler.GetAsync(run.Id, TestContext.Current.CancellationToken);

        view.ShouldNotBeNull();
        view!.Verified.ShouldBeTrue();
        view.VerificationPending.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a run with at least one pending gate, when GetAsync is called, then Verified=false and Pending=true")]
    public async Task VerificationPendingWhenAnyGateIsPendingAsync()
    {
        var db = NewDbContext();
        var records = Substitute.For<IVerificationRecordStore>();
        var artifacts = Substitute.For<IVisualArtifactStore>();
        var projectId = ProjectId.New();
        var run = Run.Create(projectId, now);
        await db.Runs.AddAsync(run, TestContext.Current.CancellationToken);

        var workItemA = WorkItem.Create(
            run.Id,
            profileKey: "verify",
            image: "ghcr.io/comuki/worker@sha256:1",
            envClass: "net10-sdk-bun",
            profilesRef: "refs/heads/main",
            brief: "{}",
            initialStatus: WorkItemStatus.Queued,
            now);
        var workItemB = WorkItem.Create(
            run.Id,
            profileKey: "verify",
            image: "ghcr.io/comuki/worker@sha256:1",
            envClass: "net10-sdk-bun",
            profilesRef: "refs/heads/main",
            brief: "{}",
            initialStatus: WorkItemStatus.Queued,
            now);
        await db.WorkItems.AddAsync(workItemA, TestContext.Current.CancellationToken);
        await db.WorkItems.AddAsync(workItemB, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        records.ListByWorkItemsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                VerificationRecord.FromEvaluation(
                    workItemA.Id,
                    "verify:generic-command-run",
                    new GateVerdictResult(GateVerdict.Pending, [], "verifier-7"),
                    now),
                VerificationRecord.FromEvaluation(
                    workItemB.Id,
                    "verify:generic-command-run",
                    new GateVerdictResult(GateVerdict.Passed, [], "verifier-7"),
                    now),
            ]);
        artifacts.ListByWorkItemsAndFilenameAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns([]);
        var handler = new GetRunVerificationViewHandler(db, records, artifacts);

        var view = await handler.GetAsync(run.Id, TestContext.Current.CancellationToken);

        view.ShouldNotBeNull();
        view!.Verified.ShouldBeFalse();
        view.VerificationPending.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a run whose work item has a cmdiff bundle member, when GetAsync is called, then the gate evidence carries the cmdiff URI")]
    public async Task EnrichEvidenceWithCmdiffWhenPresentAsync()
    {
        var db = NewDbContext();
        var records = Substitute.For<IVerificationRecordStore>();
        var artifacts = Substitute.For<IVisualArtifactStore>();
        var projectId = ProjectId.New();
        var run = Run.Create(projectId, now);
        await db.Runs.AddAsync(run, TestContext.Current.CancellationToken);

        var workItem = WorkItem.Create(
            run.Id,
            profileKey: "verify",
            image: "ghcr.io/comuki/worker@sha256:1",
            envClass: "net10-sdk-bun",
            profilesRef: "refs/heads/main",
            brief: "{}",
            initialStatus: WorkItemStatus.Queued,
            now);
        await db.WorkItems.AddAsync(workItem, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var cmdiffArtifactId = Guid.CreateVersion7();
        records.ListByWorkItemsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                VerificationRecord.FromEvaluation(
                    workItem.Id,
                    "verify:generic-command-run",
                    new GateVerdictResult(GateVerdict.Passed, [], "verifier-7"),
                    now),
            ]);
        artifacts.ListByWorkItemsAndFilenameAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new VisualArtifact
                {
                    Id = cmdiffArtifactId,
                    ProjectId = projectId.Value,
                    Filename = "changeset.diff",
                    ContentType = "text/x-diff",
                    WorkItemId = workItem.Id,
                    RunId = run.Id.Value,
                    Version = 1,
                },
            ]);
        var handler = new GetRunVerificationViewHandler(db, records, artifacts);

        var view = await handler.GetAsync(run.Id, TestContext.Current.CancellationToken);

        view.ShouldNotBeNull();
        view!.Gates.ShouldHaveSingleItem();
        view.Gates[0].EvidenceRefs.ShouldContain(cmdiffArtifactId.ToString());
    }

    [Fact(DisplayName = "Given a run whose work item has no cmdiff bundle member, when GetAsync is called, then the gate evidence list omits the cmdiff entry")]
    public async Task OmitCmdiffWhenAbsentAsync()
    {
        var db = NewDbContext();
        var records = Substitute.For<IVerificationRecordStore>();
        var artifacts = Substitute.For<IVisualArtifactStore>();
        var projectId = ProjectId.New();
        var run = Run.Create(projectId, now);
        await db.Runs.AddAsync(run, TestContext.Current.CancellationToken);

        var workItem = WorkItem.Create(
            run.Id,
            profileKey: "verify",
            image: "ghcr.io/comuki/worker@sha256:1",
            envClass: "net10-sdk-bun",
            profilesRef: "refs/heads/main",
            brief: "{}",
            initialStatus: WorkItemStatus.Queued,
            now);
        await db.WorkItems.AddAsync(workItem, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var cmdiffUri = new Uri("comuki://artifacts/gate-output");
        records.ListByWorkItemsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                VerificationRecord.FromEvaluation(
                    workItem.Id,
                    "verify:generic-command-run",
                    new GateVerdictResult(GateVerdict.Passed, [new GateEvidenceRef(GateEvidenceKind.Stdout, cmdiffUri)], "verifier-7"),
                    now),
            ]);
        artifacts.ListByWorkItemsAndFilenameAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns([]);
        var handler = new GetRunVerificationViewHandler(db, records, artifacts);

        var view = await handler.GetAsync(run.Id, TestContext.Current.CancellationToken);

        view.ShouldNotBeNull();
        view!.Gates.ShouldHaveSingleItem();
        view.Gates[0].EvidenceRefs.ShouldBe([cmdiffUri.ToString()]);
    }

    private static OrchestrationDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new OrchestrationDbContext(options);
    }
}
