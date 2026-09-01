namespace Comuki.Shared.Contracts.Plans;

/// <summary>
/// One node of a plan: a single worker-profile launch inside the run graph.
/// <paramref name="Key"/> is the plan-local id other items reference from
/// <c>DependsOn</c>; it must be unique inside the plan.
/// </summary>
/// <param name="Key">Plan-unique item key referenced by <c>DependsOn</c>.</param>
/// <param name="ProfileKey">Worker profile to launch (e.g. <c>implement</c>).</param>
/// <param name="Brief">The brief for the worker, markdown.</param>
/// <param name="DependsOn">Keys of items that must finish before this one starts.</param>
public sealed record PlanItem(string Key, string ProfileKey, string Brief, IReadOnlyList<string> DependsOn);
