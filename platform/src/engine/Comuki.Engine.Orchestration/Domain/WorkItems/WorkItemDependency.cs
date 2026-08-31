namespace Comuki.Engine.Orchestration.Domain.WorkItems;

/// <summary>
/// Edge of the plan DAG: <see cref="WorkItemId"/> may not start until
/// <see cref="DependsOnWorkItemId"/> succeeds. Owned by the run's plan;
/// composite-keyed, no separate identity.
/// </summary>
public sealed class WorkItemDependency
{
    internal WorkItemDependency()
    {
    }

    /// <summary>The dependent item.</summary>
    public Guid WorkItemId { get; private set; }

    /// <summary>The prerequisite item.</summary>
    public Guid DependsOnWorkItemId { get; private set; }

    /// <summary>Creates a dependency edge; self-references are rejected.</summary>
    /// <param name="workItemId"></param>
    /// <param name="dependsOnWorkItemId"></param>
    /// <exception cref="ArgumentException"></exception>
    public static WorkItemDependency Create(Guid workItemId, Guid dependsOnWorkItemId)
    {
        return workItemId == dependsOnWorkItemId
            ? throw new ArgumentException("a work item cannot depend on itself", nameof(dependsOnWorkItemId))
            : new WorkItemDependency
            {
                WorkItemId = workItemId,
                DependsOnWorkItemId = dependsOnWorkItemId,
            };
    }
}
