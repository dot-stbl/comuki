namespace Comuki.Shared.Contracts.Plans;

/// <summary>One step of a plan, executed by one worker with one profile.</summary>
/// <param name="Id">Node id, unique inside the plan (e.g. <c>n1</c>).</param>
/// <param name="Title">Short human title of the step.</param>
/// <param name="ProfileKey">Worker profile key from the catalog (e.g. <c>implement</c>).</param>
/// <param name="Brief">The worker brief — what this step must produce.</param>
public sealed record PlanNode(
    string Id,
    string Title,
    string ProfileKey,
    string Brief);

/// <summary>A dependency: <paramref name="To"/> starts after <paramref name="From"/> finishes.</summary>
/// <param name="From">Id of the upstream node.</param>
/// <param name="To">Id of the downstream node.</param>
public sealed record PlanEdge(
    string From,
    string To);

/// <summary>
/// The decomposition artifact the brain emits for <c>plan</c> requests —
/// a DAG of worker steps. Validated by <see cref="PlanValidator"/> before
/// it is accepted; serialized camelCase through <see cref="PlanJson"/>.
/// </summary>
/// <param name="Summary">What the plan accomplishes, one or two sentences.</param>
/// <param name="Nodes">The steps.</param>
/// <param name="Edges">The ordering dependencies between steps.</param>
public sealed record Plan(
    string Summary,
    IReadOnlyList<PlanNode> Nodes,
    IReadOnlyList<PlanEdge> Edges);
