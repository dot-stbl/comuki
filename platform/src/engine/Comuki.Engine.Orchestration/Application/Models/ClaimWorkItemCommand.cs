using Comuki.Shared.Contracts.Queue;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Application.Models;

/// <summary>
/// Claim one work item: the worker presents its id and claim labels; the
/// handler hands out a lease per <c>Orchestration:Lease</c> policy.
/// </summary>
/// <param name="WorkerId"></param>
/// <param name="Labels"></param>
public sealed record ClaimWorkItemCommand(WorkerId WorkerId, WorkItemLabels Labels);
