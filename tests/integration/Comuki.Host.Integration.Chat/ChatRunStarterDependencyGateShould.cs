using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Chat.RunStarter;
using Comuki.Shared.Contracts.Plans;
using Comuki.Shared.Contracts.Queue;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Chat;

/// <summary>
/// End-to-end proof that the real <see cref="ChatRunStarter"/> produces
/// items the queue actually gates by dependency: a dependent item starts
/// <see cref="WorkItemStatus.Blocked"/>, a worker cannot claim it until the
/// prerequisite <see cref="WorkItemStatus.Succeeded"/>s, and becomes
/// claimable right after. The queue-side mechanism (claim skips Blocked,
/// complete unblocks dependents in the same transaction) is already
/// covered in <c>WorkItemQueueShould</c> against manually-seeded items —
/// this class proves the Host-layer starter feeds it the correctly-gated
/// items through the real composition.
/// </summary>
public sealed class ChatRunStarterDependencyGateShould(HostChatServer server) : IClassFixture<HostChatServer>
{
    [Fact(DisplayName = "Given a two-node plan with one dependency edge, when the run starts, then the dependent item is not claimable until the prerequisite succeeds, then becomes claimable")]
    public async Task DependentItemIsNotClaimableUntilPrerequisiteSucceedsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Apply the plan through the real ChatRunStarter resolved out of
        // the host's DI container — proves the fix at the Host boundary,
        // not against a hand-seeded item like the queue-integration tests.
        var runId = await ApplyPlanAsync(server, new Plan(
            Summary: "gate proof",
            Nodes:
            [
                new PlanNode("n1", "prereq", "implement", "do the prerequisite thing"),
                new PlanNode("n2", "dependent", "implement", "do the dependent thing"),
            ],
            Edges: [new PlanEdge("n1", "n2")]),
            cancellationToken);

        // Re-read both created items from the host's DbContext. Cheap
        // re-confirmation that the starter produced one Queued + one
        // Blocked, before we exercise the queue claim path.
        var (prerequisite, dependent) = await LoadPrereqAndDependentAsync(server, runId, cancellationToken);
        prerequisite.Status.ShouldBe(WorkItemStatus.Queued);
        dependent.Status.ShouldBe(WorkItemStatus.Blocked);

        // Read the claim labels off the item we just created — keeps the
        // test correct regardless of how the host's ChatWorkerDefaults
        // are configured (and through WorkerImagePinning, regardless of
        // the running build version).
        var labels = new WorkItemLabels(prerequisite.Image, prerequisite.ProfilesRef, prerequisite.ProfileKey);

        // 1. First claim: gets the prerequisite (the only Queued item),
        //    never the dependent (it is Blocked, not Queued).
        var prerequisiteWorker = WorkerId.New();
        ClaimedWorkItem? firstClaim = null;
        await using (var scope = server.Services.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
            firstClaim = await queue.ClaimAsync(prerequisiteWorker, labels, DateTimeOffset.UtcNow.AddMinutes(2), DateTimeOffset.UtcNow, cancellationToken);
        }
        firstClaim.ShouldNotBeNull();
        firstClaim.WorkItemId.ShouldBe(prerequisite.Id);

        // 2. Second claim with the same labels must come back empty —
        //    the dependent is Blocked, not Queued, so the claim SQL
        //    (which filters on Status = Queued) cannot hand it out, and
        //    there is nothing else in the queue for these labels.
        ClaimedWorkItem? blockedClaim = null;
        await using (var scope = server.Services.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
            blockedClaim = await queue.ClaimAsync(WorkerId.New(), labels, DateTimeOffset.UtcNow.AddMinutes(2), DateTimeOffset.UtcNow, cancellationToken);
        }
        blockedClaim.ShouldBeNull();

        // 3. Complete the prerequisite — the queue's CompleteAsync
        //    unblocks dependents whose every prerequisite has Succeeded,
        //    in the same transaction as the Succeeded transition.
        var completed = false;
        await using (var scope = server.Services.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
            completed = await queue.CompleteAsync(
                firstClaim.WorkItemId,
                prerequisiteWorker,
                firstClaim.Generation,
                /*lang=json,strict*/ """{"summary":"prereq done"}""",
                DateTimeOffset.UtcNow,
                cancellationToken);
        }
        completed.ShouldBeTrue();

        // 4. Re-read the dependent from the host's DbContext — the
        //    unblock has landed.
        var reloadedDependent = await LoadDependentAsync(server, dependent.Id, cancellationToken);
        reloadedDependent.Status.ShouldBe(WorkItemStatus.Queued);

        // 5. Third claim: now succeeds, and lands the dependent (the
        //    only Queued item left for these labels).
        ClaimedWorkItem? dependentClaim = null;
        await using (var scope = server.Services.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IWorkItemQueue>();
            dependentClaim = await queue.ClaimAsync(WorkerId.New(), labels, DateTimeOffset.UtcNow.AddMinutes(2), DateTimeOffset.UtcNow, cancellationToken);
        }
        dependentClaim.ShouldNotBeNull();
        dependentClaim.WorkItemId.ShouldBe(dependent.Id);
    }

    /// <summary>Applies the plan through the real <see cref="ChatRunStarter"/> resolved out of the host DI.</summary>
    /// <param name="host">The host fixture — owns the composed DI container.</param>
    /// <param name="plan">The plan to apply.</param>
    /// <param name="cancellationToken"></param>
    private static async Task<RunId> ApplyPlanAsync(HostChatServer host, Plan plan, CancellationToken cancellationToken)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var starter = scope.ServiceProvider.GetRequiredService<ChatRunStarter>();
        return await starter.StartAsync(ProjectId.New(), plan, cancellationToken);
    }

    /// <summary>Re-reads the two items by Brief substring (the only string that survives the JSON round-trip as a substring anchor).</summary>
    /// <param name="host">The host fixture.</param>
    /// <param name="runId">Run to read back.</param>
    /// <param name="cancellationToken"></param>
    private static async Task<(WorkItem Prerequisite, WorkItem Dependent)> LoadPrereqAndDependentAsync(
        HostChatServer host,
        RunId runId,
        CancellationToken cancellationToken)
    {
        // Reads bypass the host's per-request subject-scope filter — a
        // directly-constructed context with no accessor is, by design, a
        // system consumer and sees everything (see OrchestrationDbContext
        // doc-comment). Mirrors ChatSessionsShould.NewOrchestrationDbAsync.
        await using var db = NewSystemDbContext(host);
        var items = await db.WorkItems.AsNoTracking()
            .Where(item => item.RunId == runId)
            .ToListAsync(cancellationToken);
        var prerequisite = items.Single(item => item.Brief.Contains("do the prerequisite thing"));
        var dependent = items.Single(item => item.Brief.Contains("do the dependent thing"));
        return (prerequisite, dependent);
    }

    /// <summary>Re-reads one work item from the host's DbContext (system consumer, no scope filter).</summary>
    /// <param name="host">The host fixture.</param>
    /// <param name="workItemId">Item to load.</param>
    /// <param name="cancellationToken"></param>
    private static async Task<WorkItem> LoadDependentAsync(HostChatServer host, Guid workItemId, CancellationToken cancellationToken)
    {
        await using var db = NewSystemDbContext(host);
        return await db.WorkItems.AsNoTracking().SingleAsync(item => item.Id == workItemId, cancellationToken);
    }

    /// <summary>Direct, accessor-less DbContext — system-consumer view, bypasses the per-request subject filter.</summary>
    /// <param name="host">The host fixture.</param>
    private static OrchestrationDbContext NewSystemDbContext(HostChatServer host)
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(options, host.ConnectionString);
        return new OrchestrationDbContext(options.Options, scopeAccessor: null);
    }
}
