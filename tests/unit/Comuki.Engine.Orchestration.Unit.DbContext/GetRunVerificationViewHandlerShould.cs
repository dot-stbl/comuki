using Comuki.Engine.Orchestration.Application.Verification;
using Comuki.Engine.Orchestration.Domain.Verification;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Runs.Verification;
using Comuki.Shared.Contracts.Verification;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.DbContext;

/// <summary>
/// Behavioural tests for <see cref="GetRunVerificationViewHandler"/>:
/// the 404-by-design on missing runs, the empty-gates list for a run
/// that has no work items, and the per-(work item, gate) flattening
/// with the lower-case verdict mapping to the wire string. The handler
/// lives in the host assembly; these tests run against an InMemory
/// EF context so the orchestration unit suite doesn't need a real
/// Postgres.
/// </summary>
public sealed class GetRunVerificationViewHandlerShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a run that does not exist, when GetAsync is called, then it returns null")]
    public async Task ReturnNullForUnknownRunAsync()
    {
        var db = NewDbContext();
        var records = Substitute.For<IVerificationRecordStore>();
        var handler = new GetRunVerificationViewHandler(db, records);

        var view = await handler.GetAsync(new RunId(Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        view.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a run with no work items, when GetAsync is called, then it returns an empty gates list")]
    public async Task ReturnEmptyGatesForRunWithoutWorkItemsAsync()
    {
        var db = NewDbContext();
        var records = Substitute.For<IVerificationRecordStore>();
        var projectId = ProjectId.New();
        var run = Domain.Runs.Run.Create(projectId, now);
        await db.Runs.AddAsync(run, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var handler = new GetRunVerificationViewHandler(db, records);

        var view = await handler.GetAsync(run.Id, TestContext.Current.CancellationToken);

        view.ShouldNotBeNull();
        view!.RunId.ShouldBe(run.Id.Value);
        view.Gates.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a run with verdicts, when GetAsync is called, then it flattens them to per-gate views")]
    public async Task FlattenVerdictsToGateViewsAsync()
    {
        var db = NewDbContext();
        var records = Substitute.For<IVerificationRecordStore>();
        var projectId = ProjectId.New();
        var run = Domain.Runs.Run.Create(projectId, now);
        await db.Runs.AddAsync(run, TestContext.Current.CancellationToken);

        var workItemA = Domain.WorkItems.WorkItem.Create(
            run.Id,
            profileKey: "verify",
            image: "ghcr.io/comuki/worker@sha256:1",
            envClass: "net10-sdk-bun",
            profilesRef: "refs/heads/main",
            brief: "{}",
            initialStatus: Domain.WorkItemStatus.Queued,
            now);
        var workItemB = Domain.WorkItems.WorkItem.Create(
            run.Id,
            profileKey: "verify",
            image: "ghcr.io/comuki/worker@sha256:1",
            envClass: "net10-sdk-bun",
            profilesRef: "refs/heads/main",
            brief: "{}",
            initialStatus: Domain.WorkItemStatus.Queued,
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
        var handler = new GetRunVerificationViewHandler(db, records);

        var view = await handler.GetAsync(run.Id, TestContext.Current.CancellationToken);

        view.ShouldNotBeNull();
        view!.RunId.ShouldBe(run.Id.Value);
        view.Gates.Count.ShouldBe(2);
        view.Gates.ShouldContain(g => g.WorkItemId == workItemA.Id && g.Verdict == "passed");
        view.Gates.ShouldContain(g => g.WorkItemId == workItemB.Id && g.Verdict == "failed");
    }

    private static OrchestrationDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new OrchestrationDbContext(options);
    }
}
