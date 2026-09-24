using System.Text.Json;
using Comuki.Engine.Compute.Options;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Bootstrap.Versioning;
using Comuki.Shared.Contracts.Plans;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Chat.RunStarter;

/// <summary>
/// Host-side plan applier (the HostRunStarter of issue #5): turns an
/// approved plan into one run and its work items — reusing the engine's
/// domain factories exactly like the queue integration seeds. Items
/// without dependencies start <c>Queued</c>, items with any start
/// <c>Blocked</c>; the plan DAG lands in <c>work_item_dependencies</c>.
/// Scoped — one context per apply.
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
/// <param name="defaults">Claim labels for chat-created items.</param>
/// <param name="buildInformation">Build identity — pins the item image to the running version.</param>
/// <param name="clock">Time source for domain stamps.</param>
public sealed class ChatRunStarter(
    OrchestrationDbContext db,
    IOptions<ChatWorkerDefaults> defaults,
    ComukiBuildInformation buildInformation,
    TimeProvider clock)
{
    /// <summary>Applies the plan; returns the created run id.</summary>
    /// <param name="projectId">Project scope of the run.</param>
    /// <param name="plan">Validated plan.</param>
    /// <param name="cancellationToken"></param>
    public async Task<RunId> StartAsync(ProjectId projectId, Plan plan, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var run = Run.Create(projectId, now);
        var itemsById = new Dictionary<string, WorkItem>(StringComparer.Ordinal);

        // Claim matching compares the item's image with the worker's
        // labels for equality — the supervisor pins its spawn through
        // WorkerImagePinning, so the item side must resolve through the
        // same function or no worker ever matches (release contract,
        // see WorkerImagePinning).
        var image = WorkerImagePinning.Resolve(defaults.Value.Image, buildInformation);

        // Nodes that appear as a `To` in the DAG have >=1 prerequisite and
        // must start Blocked; nodes that never appear as a `To` have zero
        // prerequisites and start Queued. Matching the id comparer used by
        // `itemsById` keeps the lookup and the membership check coherent.
        var blockedNodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in plan.Edges)
        {
            blockedNodeIds.Add(edge.To);
        }

        foreach (var node in plan.Nodes)
        {
            var initialStatus = blockedNodeIds.Contains(node.Id)
                ? WorkItemStatus.Blocked
                : WorkItemStatus.Queued;
            var workItem = WorkItem.Create(
                run.Id,
                node.ProfileKey,
                image,
                defaults.Value.ProfilesRef,
                ChatItemBrief.ToJson(node.Brief),
                initialStatus,
                now);
            itemsById[node.Id] = workItem;
            db.WorkItems.Add(workItem);
        }

        foreach (var edge in plan.Edges)
        {
            db.WorkItemDependencies.Add(
                WorkItemDependency.Create(itemsById[edge.To].Id, itemsById[edge.From].Id));
        }

        db.Runs.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run.Id;
    }
}

/// <summary>Plan item brief → worker brief jsonb (the <c>goal</c> shape the worker runtime reads).</summary>
file static class ChatItemBrief
{
    public static string ToJson(string brief)
    {
        return JsonSerializer.Serialize(new ChatItemGoal(brief), JsonSerializerOptions.Web);
    }
}

/// <summary>Worker brief payload — mirrors the queue integration seeds.</summary>
/// <param name="Goal">The worker goal (the plan item brief).</param>
internal sealed record ChatItemGoal(string Goal);
