namespace Comuki.Shared.Contracts.Plans;

/// <summary>
/// A brain-produced plan: the list of worker-profile launches plus their
/// dependencies. Shared by every producer/consumer (chat graph confirm→act,
/// the brain, future replan) — one validated shape everywhere.
/// </summary>
/// <param name="Items">Plan items; must contain at least one.</param>
public sealed record Plan(IReadOnlyList<PlanItem> Items);
