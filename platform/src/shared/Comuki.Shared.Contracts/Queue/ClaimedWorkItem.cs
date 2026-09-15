using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Queue;

/// <summary>
/// A work item handed to a worker by a successful claim. The brief is the raw
/// JSON the plan applied; <see cref="LeaseUntil"/> is when the worker loses
/// the item unless it heartbeats. <see cref="ProjectId"/> is the parent run's
/// project — the scope every project-bound worker call (memory, knowledge)
/// is confined to.
/// </summary>
/// <param name="WorkItemId"></param>
/// <param name="RunId"></param>
/// <param name="ProjectId">Project the parent run belongs to.</param>
/// <param name="ProfileKey"></param>
/// <param name="Brief"></param>
/// <param name="LeaseUntil"></param>
/// <param name="Attempt"></param>
public sealed record ClaimedWorkItem(
    Guid WorkItemId,
    RunId RunId,
    Guid ProjectId,
    string ProfileKey,
    string Brief,
    DateTimeOffset LeaseUntil,
    int Attempt);
