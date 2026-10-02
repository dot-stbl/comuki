using Comuki.Modules.Procedures.Domain.Layering.Helpers;
using Comuki.Modules.Procedures.Domain.Layering.Model;
using Comuki.Modules.Procedures.Domain.Layering.Results;

namespace Comuki.Modules.Procedures.Domain.Layering;

/// <summary>
/// Structural merge of platform defaults, project policy, and the
/// repository bindings that select this procedure (design decision 2).
/// The merge is pure: no I/O, no DI, no exceptions for expected
/// outcomes. Conflicts are loud refusals
/// (<see cref="ProcedureLayeringException"/>) and carry the offending
/// element in the message; the individual checks live in
/// <see cref="LayeringRefusals"/>, the set and floor
/// computations in <see cref="LayeringMerge"/>.
///
/// <para>
/// Merge order:
/// <list type="number">
///   <item>Repository bindings are checked first — a binding with
///   non-empty <see cref="RepositoryBinding.AdditionalNodes"/> or
///   <see cref="RepositoryBinding.AdditionalEdges"/> is refused
///   (spec requirement: "repository binding SHALL NOT introduce
///   control flow").</item>
///   <item>The platform's <see cref="PlatformDefaults.AllowedKindKeys"/>
///   is the seed set; the project's <see cref="ProjectPolicy.AdditionalAllowedKindKeys"/>
///   is unioned in.</item>
///   <item>Floors are taken from the platform's defaults and
///   tightened by the project's overrides when the overrides lower
///   the platform's bound (a project's <c>MaxGenerationsOverride</c>
///   must be no greater than the platform's
///   <see cref="PlatformDefaults.MaxGenerations"/>).</item>
///   <item>The project's graph is validated against the merged
///   allowlist and against itself (duplicate ids, edge endpoints).</item>
///   <item>The layered procedure is returned.</item>
/// </list>
/// </para>
/// </summary>
public static class ProcedureLayering
{
    /// <summary>
    /// Performs the layered merge per design decision 2. Throws
    /// <see cref="ProcedureLayeringException"/> with the offending
    /// element named in the message when any layer introduces a
    /// conflict.
    /// </summary>
    /// <param name="platform">The platform's default floors and allowlist.</param>
    /// <param name="project">The project's graph plus optional tightening.</param>
    /// <param name="repositoryBindings">
    /// The repository bindings selecting this procedure. Empty list
    /// when no repository has bound yet.
    /// </param>
    /// <param name="bindings">
    /// Convenience alias for <paramref name="repositoryBindings"/> —
    /// the caller decides which to use.
    /// </param>
    public static LayeredProcedure Merge(
        PlatformDefaults platform,
        ProjectPolicy project,
        IReadOnlyList<RepositoryBinding>? repositoryBindings = null,
        IReadOnlyList<RepositoryBinding>? bindings = null)
    {
        var effectiveBindings = repositoryBindings ?? bindings ?? [];

        LayeringRefusals.RefuseBindingsThatAddControlFlow(effectiveBindings);

        var allowed = LayeringMerge.MergeAllowedKindKeys(platform, project);
        var floors = LayeringMerge.MergeFloors(platform, project);

        var nodes = project.Definition.Nodes;
        var edges = project.Definition.Edges;
        LayeringRefusals.RefuseDuplicateNodeIds(nodes);
        LayeringRefusals.RefuseEdgesPointingAtUnknownNodes(nodes, edges);
        LayeringRefusals.RefuseNodesWithDisallowedKindKeys(nodes, allowed);

        return new LayeredProcedure(
            ProcedureName: project.Definition.Name,
            Nodes: nodes,
            Edges: edges,
            AllowedKindKeys: allowed,
            MaxGenerations: floors.MaxGenerations,
            MinApprovals: floors.MinApprovals,
            RepositoryBindings: effectiveBindings);
    }
}
