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
/// Fact 1 covers WS9, WS1, WS2/WS7 and WS3 on a single run. Fact 2
/// covers WS4/WS5 (cancel fences stale-worker complete) on a second,
/// independent run. The two facts share no state beyond the host.
/// </para>
/// </remarks>
[Collection(nameof(CrownScenarioCollection))]
public sealed class CrownScenarioShould(CrownScenarioHost host) : IAsyncLifetime
{
    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        // Hold the boot-time migration + intake-bridge idle sweep from
        // racing into Fact 1's first webhook POST (both TargetInvocation
        // windows open for the very first claim) — the WebhooksShould
        // suite calls no equivalent, but it doesn't have a webhook
        // backed by a watch rule + on-the-fly bridge worker either; a
        // 1-second sleep is the cheap, conservative settlement.
        await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    [Fact(DisplayName = "Given an admission webhook delivered twice, a dependent plan, and a concurrent terminal race, then WS9/WS1/WS2/WS7/WS3 all hold on one Run")]
    public async Task ProvesAdmissionDependencyAndExactlyOnceFinalizationAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = ProjectId.New().Value;
        using var browser = await host.CreateBrowserClientAsync();
        using var anonymous = host.CreateAnonymousClient();
        var webhookPath = await CrownScenarioHost.ProvisionWebhookAsync(browser, projectId, "comuki");

        // WS9: same payload bytes + same deliveryId twice yields exactly one Run.
        var payload = CrownScenarioHost.BuildGithubIssuePayload(
            title: "ws10 crown: dependency + terminal race",
            body: "drives WS1 unblock + WS2/WS7 exactly-once finalization + WS3 no-retry edge on one Run",
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

        // Exactly one run for this project.
        var runs = await LoadRunsForProjectAsync(projectId);
        runs.Count.ShouldBe(1);
        var run = runs[0];
        run.Id.Value.ShouldNotBe(Guid.Empty);

        // WS1: the single intake-created item is the prerequisite; seed two
        // dependents on the SAME run that point at it (DAG edge + DAG edge),
        // both initially Blocked, with claim labels matching CrownScenarioHost.EntryLabels
        // so one ClaimAsync covers the prerequisite AND both dependents after
        // they unblock.
        var items = await LoadWorkItemsForRunAsync(run.Id.Value);
        items.Count.ShouldBe(1);
        var entryItem = items[0];
        entryItem.Status.ShouldBe(WorkItemStatus.Queued);

        var seeded = await SeedBlockedDependentsAsync(
            runId: run.Id.Value,
            count: 2,
            dependsOnWorkItemId: entryItem.Id);
        var dependentA = seeded[0];
        var dependentB = seeded[1];

        var reloadedDependents = await LoadWorkItemsForRunAsync(run.Id.Value);
        reloadedDependents.Count(item => item.Id == dependentA.Id).ShouldBe(1);
        reloadedDependents.Count(item => item.Id == dependentB.Id).ShouldBe(1);
        reloadedDependents.Single(item => item.Id == dependentA.Id).Status.ShouldBe(WorkItemStatus.Blocked);
        reloadedDependents.Single(item => item.Id == dependentB.Id).Status.ShouldBe(WorkItemStatus.Blocked);

        // Drive the prerequisite to Succeeded — both dependents must unblock
        // in the same transaction (the SQL WS1 invariant the UnblockDependents
        // path enforces); claim + complete use one in-process worker scope.
        var entryWorker = WorkerId.New();
        var entryClaim = await ClaimAsync(entryWorker, CrownScenarioHost.EntryLabels);
        entryClaim.ShouldNotBeNull();
        entryClaim.WorkItemId.ShouldBe(entryItem.Id);
        var entryCompleted = await CompleteAsync(entryItem.Id, entryWorker, entryClaim.Generation, summary: "entry done");
        entryCompleted.ShouldBeTrue();

        var afterEntry = await LoadWorkItemsForRunAsync(run.Id.Value);
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

        var finalRun = (await LoadRunsForProjectAsync(projectId)).Single();
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

        // Exactly one orchestration.run.terminated.v1 outbox row, status=Failed.
        var outboxMessages = await LoadOutboxMessagesAsync();
        var terminatedMessages = outboxMessages.Where(static message => message.Type == RunEventTypes.RunTerminatedV1).ToList();
        terminatedMessages.ShouldHaveSingleItem("WS2/WS7: exactly one orchestration.run.terminated.v1 outbox row");
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

    /// <summary>Direct-seeds <paramref name="count"/> Blocked dependents on <paramref name="runId"/> that
    /// all depend on <paramref name="dependsOnWorkItemId"/>. Mirrors the shape of
    /// WorkItemQueueShould.SeedBlockedDependentAsync.</summary>
    private async Task<List<WorkItem>> SeedBlockedDependentsAsync(
        Guid runId,
        int count,
        Guid dependsOnWorkItemId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = host.CreateOrchestrationDb();
        var now = DateTimeOffset.UtcNow;
        var dependents = Enumerable.Range(0, count)
            .Select(index => WorkItem.Create(
                new RunId(runId),
                profileKey: CrownScenarioHost.EntryLabels.ProfileKey,
                image: CrownScenarioHost.EntryLabels.Image,
                profilesRef: CrownScenarioHost.EntryLabels.ProfilesRef,
                brief: /*lang=json,strict*/ $$"""{"goal":"crown-dependent-{{index}}"}""",
                initialStatus: WorkItemStatus.Blocked,
                now: now.AddMilliseconds(index)))
            .ToList();
        db.WorkItems.AddRange(dependents);
        foreach (var dependent in dependents)
        {
            db.WorkItemDependencies.Add(WorkItemDependency.Create(dependent.Id, dependsOnWorkItemId));
        }

        await db.SaveChangesAsync(cancellationToken);
        return dependents;
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
