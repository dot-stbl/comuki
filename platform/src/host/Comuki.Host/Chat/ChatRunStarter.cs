using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Contracts.Plans;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Chat;

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
/// <param name="clock">Time source for domain stamps.</param>
public sealed class ChatRunStarter(
    OrchestrationDbContext db,
    IOptions<ChatWorkerDefaults> defaults,
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
        var itemsByKey = new Dictionary<string, WorkItem>(StringComparer.Ordinal);

        foreach (var item in plan.Items)
        {
            var workItem = WorkItem.Create(
                run.Id,
                item.ProfileKey,
                defaults.Value.Image,
                defaults.Value.ProfilesRef,
                ChatItemBrief.ToJson(item.Brief),
                item.DependsOn.Count == 0 ? WorkItemStatus.Queued : WorkItemStatus.Blocked,
                now);
            itemsByKey[item.Key] = workItem;
            db.WorkItems.Add(workItem);
        }

        foreach (var item in plan.Items)
        {
            foreach (var dependsOn in item.DependsOn)
            {
                db.WorkItemDependencies.Add(
                    WorkItemDependency.Create(itemsByKey[item.Key].Id, itemsByKey[dependsOn].Id));
            }
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
