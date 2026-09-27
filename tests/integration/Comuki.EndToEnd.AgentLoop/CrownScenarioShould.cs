using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Exceptions;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Outbox;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Host;
using Comuki.Host.Chat.RunStarter;
using Comuki.Shared.Contracts.Plans;
using Comuki.Shared.Contracts.Queue;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.EndToEnd.AgentLoop;

/// <summary>
/// The WS10 crown scenario proof — one suite that exercises, together, the
/// five execution-spine invariants the <c>add-mission-cowork</c> epic
/// promises (issue #87 sub-stabs WS1/WS2/WS4/WS5/WS7/WS9), driven through
/// the REAL host composition (<see cref="HostComposer"/>)
/// against the REAL Postgres (via <see cref="CrownScenarioHost"/>).
/// </summary>
/// <remarks>
/// <para>
/// Container-based workers (<see cref="AgentLoopHost"/> +
/// <c>Comuki.AgentTest.Runner</c>'s <c>DockerComputeProvider</c>) are
/// blocked in this Podman/WSL2 sandbox by issues #152/#153; see
/// <c>CrownScenarioHost</c>'s remarks and
/// <c>.agents/rules/process/local-test-runtime.md</c>. "Workers" in this
/// suite are therefore in-process <see cref="IWorkItemQueue"/> calls
/// against the real composition — the same proven pattern
/// <c>RunDecisionsEndpointShould.CancelFencesLiveWorkItemAsync</c> and
/// the entire <c>Comuki.Engine.Orchestration.Integration.Queue</c>
/// suite already use.
/// </para>
/// <para>
/// Fact 1 covers WS9 (its own Run, via the real webhook path) and WS1/
/// WS2/WS7/WS3 (a second Run, via the real <see cref="ChatRunStarter"/>
/// plan materialization — the coordinator's crown review asked for this
/// instead of a raw EF-seeded dependent plan, so the DAG-to-Blocked-item
/// path under test is the actual production code, not a test double of
/// it). Fact 2 covers WS4/WS5 (cancel fences stale-worker complete) on a
/// third, independent run (its own Run, via the webhook path). No two
/// facts, and no two Runs within Fact 1, share state.
/// </para>
/// </remarks>
[Collection(nameof(CrownScenarioCollection))]
public sealed class CrownScenarioShould(CrownScenarioHost host)
{
    [Fact(DisplayName = "Given an admission webhook delivered twice, a dependent plan, and a concurrent terminal race, then WS9/WS1/WS2/WS7/WS3 all hold")]
    public async Task ProvesAdmissionDependencyAndExactlyOnceFinalizationAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // WS9: same payload bytes + same deliveryId twice, through the real
        // webhook endpoint, yields exactly one Run. Both the webhook's own
        // admission and (below) ChatRunStarter's plan apply are synchronous
        // DB writes awaited fully before the next line runs — no eventual
        // consistency here, so no settle delay/condition wait is needed
        // anywhere in this fact.
        var admissionProjectId = ProjectId.New().Value;
        using var browser = await host.CreateBrowserClientAsync();
        using var anonymous = host.CreateAnonymousClient();
        var webhookPath = await CrownScenarioHost.ProvisionWebhookAsync(browser, admissionProjectId, "comuki");

        var payload = CrownScenarioHost.BuildGithubIssuePayload(
            title: "ws10 crown: admission idempotency",
            body: "drives WS9 — an admission retry must not double-launch",
            labels: ["comuki"],
            issueNumber: 1042,
            repoFullName: "comuki/crown-scenario");
        var deliveryId = "crown-ws9-" + Guid.NewGuid().ToString("N");

        var first = await CrownScenarioHost.PostWebhookAsync(anonymous, webhookPath, payload, deliveryId);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(cancellationToken));
        using (var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync(cancellationToken)))
        {
            firstDoc.RootElement.GetProperty("outcome").GetString().ShouldBe("admitted");
        }

        // Replay — same deliveryId, must be a 200 replay, no second Run.
        var replay = await CrownScenarioHost.PostWebhookAsync(anonymous, webhookPath, payload, deliveryId);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync(cancellationToken));
        using (var replayDoc = JsonDocument.Parse(await replay.Content.ReadAsStringAsync(cancellationToken)))
        {
            replayDoc.RootElement.GetProperty("outcome").GetString().ShouldBe("replay");
        }

        var admissionRun = (await LoadRunsForProjectAsync(admissionProjectId)).ShouldHaveSingleItem("WS9: a replayed admission delivery must not double-launch a run");

        // Retire the webhook-created item immediately — it plays no further
        // role in this fact, and ClaimSql's FIFO has no run-id scoping (only
        // label + status matching), so leaving it Queued would let a later
        // ClaimAsync call below (or Fact 2's) pick it up ahead of — or
        // instead of — the item that call actually means to claim.
        var admissionItem = (await LoadWorkItemsForRunAsync(admissionRun.Id.Value)).ShouldHaveSingleItem();
        var admissionWorker = WorkerId.New();
        var admissionClaim = await ClaimAsync(admissionWorker, CrownScenarioHost.EntryLabels);
        admissionClaim.ShouldNotBeNull();
        admissionClaim.WorkItemId.ShouldBe(admissionItem.Id);
        (await CompleteAsync(admissionItem.Id, admissionWorker, admissionClaim.Generation, summary: "admission fixture retired")).ShouldBeTrue();

        // WS1: a real ChatRunStarter plan apply — prereq has no incoming
        // edge (starts Queued); dep-a/dep-b each depend on prereq (start
        // Blocked). This is the exact DAG-to-item materialization
        // ChatRunStarter.StartAsync does in production (fix #166), not a
        // raw EF seed of the same shape.
        var planProjectId = ProjectId.New();
        var plan = new Plan(
            Summary: "crown dependency plan",
            Nodes:
            [
                new PlanNode("prereq", "Prerequisite", "implement", "crown prerequisite brief"),
                new PlanNode("dep-a", "Dependent A", "implement", "crown dependent-a brief"),
                new PlanNode("dep-b", "Dependent B", "implement", "crown dependent-b brief"),
            ],
            Edges:
            [
                new PlanEdge("prereq", "dep-a"),
                new PlanEdge("prereq", "dep-b"),
            ]);
        var planRunId = await StartChatPlanAsync(planProjectId, plan);

        var items = await LoadWorkItemsForRunAsync(planRunId.Value);
        items.Count.ShouldBe(3);
        var entryItem = items.Single(item => item.Brief.Contains("crown prerequisite brief", StringComparison.Ordinal));
        var dependentA = items.Single(item => item.Brief.Contains("crown dependent-a brief", StringComparison.Ordinal));
        var dependentB = items.Single(item => item.Brief.Contains("crown dependent-b brief", StringComparison.Ordinal));
        entryItem.Status.ShouldBe(WorkItemStatus.Queued);
        dependentA.Status.ShouldBe(WorkItemStatus.Blocked);
        dependentB.Status.ShouldBe(WorkItemStatus.Blocked);

        // Drive the prerequisite to Succeeded — both dependents must unblock
        // in the same transaction (the SQL WS1 invariant the UnblockDependents
        // path enforces); claim + complete use one in-process worker scope.
        var entryWorker = WorkerId.New();
        var entryClaim = await ClaimAsync(entryWorker, CrownScenarioHost.EntryLabels);
        entryClaim.ShouldNotBeNull();
        entryClaim.WorkItemId.ShouldBe(entryItem.Id);
        var entryCompleted = await CompleteAsync(entryItem.Id, entryWorker, entryClaim.Generation, summary: "entry done");
        entryCompleted.ShouldBeTrue();

        var afterEntry = await LoadWorkItemsForRunAsync(planRunId.Value);
        afterEntry.Single(item => item.Id == entryItem.Id).Status.ShouldBe(WorkItemStatus.Succeeded);
        var dependentAReloaded = afterEntry.Single(item => item.Id == dependentA.Id);
        var dependentBReloaded = afterEntry.Single(item => item.Id == dependentB.Id);
        dependentAReloaded.Status.ShouldBe(WorkItemStatus.Queued, "WS1: dependent A must unblock in the same transaction as the prerequisite's success");
        dependentBReloaded.Status.ShouldBe(WorkItemStatus.Queued, "WS1: dependent B must unblock in the same transaction as the prerequisite's success");

        // WS2/WS7: two real concurrent transactions on separate scopes/connections
        // that race to finalize the run to one terminal status; the
        // exactly-once finalization invariant must hold (mirrors
        // RunJournalShould.FinalizeExactlyOnceOnConcurrentCompleteAndFailAsync's
        // shape — start both without awaiting either first, then await both).
        var workerA = WorkerId.New();
        var workerB = WorkerId.New();
        var claimA = await ClaimAsync(workerA, CrownScenarioHost.EntryLabels);
        var claimB = await ClaimAsync(workerB, CrownScenarioHost.EntryLabels);
        claimA.ShouldNotBeNull();
        claimB.ShouldNotBeNull();
        // Each claim picks one of the two newly-unblocked dependents; either
        // shape is fine, but they must be distinct items.
        claimA.WorkItemId.ShouldNotBe(claimB.WorkItemId);

        var completeOne = CompleteAsync(claimA.WorkItemId, workerA, claimA.Generation, summary: "dependent done");
        var failTwo = FailAsync(claimB.WorkItemId, workerB, claimB.Generation, reason: "boom");
        var completeOneResult = await completeOne;
        var failTwoResult = await failTwo;

        completeOneResult.ShouldBeTrue();
        failTwoResult.ShouldBeTrue();

        var finalRun = (await LoadRunsForProjectAsync(planProjectId.Value)).Single();
        finalRun.Status.ShouldBe(RunStatus.Failed);

        // Exactly one run.status_changed whose `to` is a terminal status
        // (Succeeded or Failed). The other run.status_changed is the
        // activation (Queued -> Running) on first claim, which is NOT a
        // finalization.
        var events = await LoadRunEventsForRunAsync(finalRun.Id.Value);
        var statusEvents = events.Where(static runEvent => runEvent.Type == RunEventTypes.RunStatusChanged).ToList();
        var finalizations = statusEvents.Where(static runEvent =>
        {
            using var payloadDoc = JsonDocument.Parse(runEvent.Payload);
            return payloadDoc.RootElement.GetProperty("to").GetString() is "Succeeded" or "Failed";
        }).ToList();
        finalizations.ShouldHaveSingleItem("WS2/WS7: exactly one terminal run.status_changed event");

        // Exactly one orchestration.run.terminated.v1 outbox row FOR THIS RUN.
        // The retired WS9 admission item above finalizes its own,
        // unrelated run and stages its own outbox row — LoadOutboxMessagesAsync
        // reads the whole table, so this filters to planRunId to keep the
        // assertion about the run this fact's WS2/WS7 race actually finalized.
        var outboxMessages = await LoadOutboxMessagesAsync();
        var terminatedMessages = outboxMessages
            .Where(message => message.Type == RunEventTypes.RunTerminatedV1)
            .Where(message =>
            {
                using var candidatePayload = JsonDocument.Parse(message.Payload);
                return candidatePayload.RootElement.GetProperty("runId").GetGuid() == finalRun.Id.Value;
            })
            .ToList();
        terminatedMessages.ShouldHaveSingleItem("WS2/WS7: exactly one orchestration.run.terminated.v1 outbox row for this run");
        using (var terminatedPayload = JsonDocument.Parse(terminatedMessages[0].Payload))
        {
            terminatedPayload.RootElement.GetProperty("runId").GetGuid().ShouldBe(finalRun.Id.Value);
            terminatedPayload.RootElement.GetProperty("status").GetString().ShouldBe("Failed");
        }

        // WS3: Failed is a true terminal Run status — no Failed -> Queued edge.
        RunTransitions.IsLegal(RunStatus.Failed, RunStatus.Queued).ShouldBeFalse();
        Should.Throw<OrchestrationDomainException>(
            () => finalRun.TransitionTo(RunStatus.Queued, DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given a cancelled Run with a live claimed WorkItem, then the stale-generation worker's complete is rejected (WS4/WS5)")]
    public async Task StaleWorkerFencedAfterCancelAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = ProjectId.New().Value;
        using var browser = await host.CreateBrowserClientAsync();
        using var anonymous = host.CreateAnonymousClient();
        var webhookPath = await CrownScenarioHost.ProvisionWebhookAsync(browser, projectId, "comuki");

        // A second, distinct ticket — fresh issue number / deliveryId — so
        // this fact owns its own Run and doesn't share state with Fact 1.
        var payload = CrownScenarioHost.BuildGithubIssuePayload(
            title: "ws10 crown: cancel fences stale worker",
            body: "drives WS4/WS5 cancel-fences-live-claimed-item invariant",
            labels: ["comuki"],
            issueNumber: 2087,
            repoFullName: "comuki/crown-scenario");
        var deliveryId = "crown-ws4-" + Guid.NewGuid().ToString("N");

        var opened = await CrownScenarioHost.PostWebhookAsync(anonymous, webhookPath, payload, deliveryId);
        opened.StatusCode.ShouldBe(HttpStatusCode.OK, await opened.Content.ReadAsStringAsync(cancellationToken));

        var runs = await LoadRunsForProjectAsync(projectId);
        var run = runs.ShouldHaveSingleItem();
        var items = await LoadWorkItemsForRunAsync(run.Id.Value);
        var fenceItem = items.ShouldHaveSingleItem();
        fenceItem.Status.ShouldBe(WorkItemStatus.Queued);

        // In-process worker claims the live item under the run's initial
        // generation; capture the WorkerId + the generation the worker was
        // leased under so the stale-generation Complete can be sent below.
        var staleWorker = WorkerId.New();
        var claimed = await ClaimAsync(staleWorker, CrownScenarioHost.EntryLabels);
        claimed.ShouldNotBeNull();
        claimed.WorkItemId.ShouldBe(fenceItem.Id);
        var staleGeneration = claimed.Generation;
        staleGeneration.ShouldBeGreaterThan(0);

        var runBeforeCancel = (await LoadRunsForProjectAsync(projectId)).Single();
        runBeforeCancel.Status.ShouldBe(RunStatus.Running);

        // Operator cancels the run via the real /cancel endpoint.
        var cancelResponse = await browser.PostAsJsonAsync(
            $"/api/v1/runs/{run.Id.Value}/cancel",
            new { reason = "ws10 crown: fence stale worker" },
            cancellationToken);
        cancelResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // WS4/WS5: the worker's complete under the OLD generation must be
        // rejected (generation guard in WorkItemQueue's CompleteAsync SQL).
        var staleComplete = await CompleteAsync(fenceItem.Id, staleWorker, staleGeneration, summary: "stale");
        staleComplete.ShouldBeFalse("WS4/WS5: stale-generation complete must be rejected after cancel");

        var runAfter = (await LoadRunsForProjectAsync(projectId)).Single();
        runAfter.Status.ShouldBe(RunStatus.Cancelled);
        runAfter.Generation.ShouldBeGreaterThan(staleGeneration);

        var itemAfter = (await LoadWorkItemsForRunAsync(run.Id.Value)).Single();
        itemAfter.Status.ShouldBe(WorkItemStatus.Running, "WS4/WS5: cancel fences the work item but does not stop the lease mid-flight");
        itemAfter.Generation.ShouldNotBe(staleGeneration);
    }

    /// <summary>Returns the run(s) for <paramref name="projectId"/> — system scope via no accessor (HostIntakeServer.NewSystemDbContext precedent).</summary>
    private async Task<List<Run>> LoadRunsForProjectAsync(Guid projectId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = host.CreateOrchestrationDb();
        return await db.Runs.AsNoTracking()
            .Where(run => run.ProjectId == new ProjectId(projectId))
            .OrderBy(run => run.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Returns the WorkItems for a run, oldest first.</summary>
    private async Task<List<WorkItem>> LoadWorkItemsForRunAsync(Guid runId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = host.CreateOrchestrationDb();
        return await db.WorkItems.AsNoTracking()
            .Where(item => item.RunId == new RunId(runId))
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Returns the RunEvent rows for a run, oldest first.</summary>
    private async Task<List<RunEvent>> LoadRunEventsForRunAsync(Guid runId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = host.CreateOrchestrationDb();
        return await db.RunEvents.AsNoTracking()
            .Where(runEvent => runEvent.RunId == new RunId(runId))
            .OrderBy(runEvent => runEvent.OccurredAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Loads every outbox row this run's processing enqueued (every test in this suite
    /// owns exactly one Run, so a "load everything" reads only the rows this test produced).</summary>
    private async Task<List<OutboxMessage>> LoadOutboxMessagesAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = host.CreateOrchestrationDb();
        return await db.OutboxMessages.AsNoTracking().ToListAsync(cancellationToken);
    }

    /// <summary>Applies a plan through the real <see cref="ChatRunStarter"/> — the
    /// production DAG-to-WorkItem materialization (dependents start Blocked,
    /// see fix #166) — in its own scope, mirroring <see cref="ClaimAsync"/>'s shape.</summary>
    private async Task<RunId> StartChatPlanAsync(ProjectId projectId, Plan plan)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = host.Services.CreateAsyncScope();
        using var systemScope = scope.ServiceProvider.GetRequiredService<ISubjectScopeAccessor>().AsSystem("crown-scenario");
        var starter = scope.ServiceProvider.GetRequiredService<ChatRunStarter>();
        return await starter.StartAsync(projectId, plan, cancellationToken);
    }

    /// <summary>In-process worker claim — mirrors <see cref="AgentLoopHost"/>'s in-process-worker
    /// precedent and <c>RunDecisionsEndpointShould.SeedRunningRunWithClaimedItemAsync</c>'s shape.
    /// Caller owns the <paramref name="workerId"/> — the same value must be passed to
    /// <see cref="CompleteAsync"/> / <see cref="FailAsync"/> for the item this claim returned, because
    /// <see cref="ClaimedWorkItem"/> carries the generation but not the worker id (it does not yet
    /// exist when the record is shaped — see <c>ClaimedWorkItem</c>'s primary-ctor field list).</summary>
    private async Task<ClaimedWorkItem?> ClaimAsync(WorkerId workerId, WorkItemLabels labels)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        await using var scope = host.Services.CreateAsyncScope();
        using var systemScope = scope.ServiceProvider.GetRequiredService<ISubjectScopeAccessor>().AsSystem("crown-scenario");
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        return await queue.ClaimAsync(workerId, labels, now.AddMinutes(2), now, cancellationToken);
    }

    /// <summary>In-process worker complete — the WS4/WS5 stale-generation guard in
    /// <c>WorkItemQueueSql.CompleteAsync</c> is what Fact 2 actually exercises through this call.</summary>
    private async Task<bool> CompleteAsync(Guid workItemId, WorkerId workerId, int generation, string summary)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        await using var scope = host.Services.CreateAsyncScope();
        using var systemScope = scope.ServiceProvider.GetRequiredService<ISubjectScopeAccessor>().AsSystem("crown-scenario");
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        var payload = JsonSerializer.Serialize(new { summary }, JsonSerializerOptions.Web);
        return await queue.CompleteAsync(workItemId, workerId, generation, payload, now, cancellationToken);
    }

    /// <summary>In-process worker fail — counterpart of <see cref="CompleteAsync"/>.</summary>
    private async Task<bool> FailAsync(Guid workItemId, WorkerId workerId, int generation, string reason)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        await using var scope = host.Services.CreateAsyncScope();
        using var systemScope = scope.ServiceProvider.GetRequiredService<ISubjectScopeAccessor>().AsSystem("crown-scenario");
        var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
        return await queue.FailAsync(workItemId, workerId, generation, reason, now, cancellationToken);
    }
}
