using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Comuki.Modules.Procedures.Domain.Layering.Model;

namespace Comuki.Modules.Procedures.Domain.Layering.Helpers;

/// <summary>
/// The merge's refusal checks, extracted per the no-private-methods
/// rule — each check is a pure function over its arguments that throws
/// <see cref="ProcedureLayeringException"/> naming the offending
/// element. The merge in <see cref="ProcedureLayering"/> orchestrates
/// them; every check is testable in isolation.
/// </summary>
internal static class LayeringRefusals
{
    /// <summary>
    /// Refuses a binding that tries to introduce nodes or edges —
    /// bindings may only select (spec requirement: "repository binding
    /// SHALL NOT introduce control flow").
    /// </summary>
    /// <param name="bindings">The repository bindings selecting the procedure.</param>
    public static void RefuseBindingsThatAddControlFlow(IReadOnlyList<RepositoryBinding> bindings)
    {
        foreach (var binding in bindings)
        {
            if (binding.AdditionalNodes is { Count: > 0 }
                || binding.AdditionalEdges is { Count: > 0 })
            {
                throw new ProcedureLayeringException(
                    ProcedureLayeringException.RepositoryBindingAddsControlFlow,
                    $"Repository binding '{binding.RepositoryId}' tries to introduce "
                    + $"{binding.AdditionalNodes?.Count ?? 0} node(s) and "
                    + $"{binding.AdditionalEdges?.Count ?? 0} edge(s); bindings may "
                    + "only select which procedure the repository runs against — control flow is layered in "
                    + "from the platform defaults and the project policy (design decision 2).");
            }
        }
    }

    /// <summary>Refuses a project whose MaxGenerations override loosens the platform's floor.</summary>
    /// <param name="platform">The platform's default floors.</param>
    /// <param name="project">The project's policy with optional overrides.</param>
    public static void RefuseProjectLooseningMaxGenerations(PlatformDefaults platform, ProjectPolicy project)
    {
        if (project.MaxGenerationsOverride is { } projectMax && projectMax > platform.MaxGenerations)
        {
            throw new ProcedureLayeringException(
                ProcedureLayeringException.RepositoryBindingAddsControlFlow,
                $"Project's MaxGenerationsOverride '{projectMax}' exceeds the platform's MaxGenerations floor '{platform.MaxGenerations}'.");
        }
    }

    /// <summary>Refuses a project whose MinApprovals override loosens the platform's floor.</summary>
    /// <param name="platform">The platform's default floors.</param>
    /// <param name="project">The project's policy with optional overrides.</param>
    public static void RefuseProjectLooseningMinApprovals(PlatformDefaults platform, ProjectPolicy project)
    {
        if (project.MinApprovalsOverride is { } projectMin && projectMin < platform.MinApprovals)
        {
            throw new ProcedureLayeringException(
                ProcedureLayeringException.RepositoryBindingAddsControlFlow,
                $"Project's MinApprovalsOverride '{projectMin}' is below the platform's MinApprovals floor '{platform.MinApprovals}'.");
        }
    }

    /// <summary>Refuses a graph that declares the same node id twice (conflict-as-error, design decision 2).</summary>
    /// <param name="nodes">The procedure graph's nodes.</param>
    public static void RefuseDuplicateNodeIds(IReadOnlyList<ProcedureNode> nodes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (!seen.Add(node.Id))
            {
                throw new ProcedureLayeringException(
                    ProcedureLayeringException.DuplicateNodeId,
                    $"Procedure graph declares node id '{node.Id}' (kind '{node.KindKey}') more than once; node ids must be unique inside a procedure definition (conflict-as-error per design decision 2).");
            }
        }
    }

    /// <summary>Refuses an edge whose source or destination node id is absent from the graph.</summary>
    /// <param name="nodes">The procedure graph's nodes.</param>
    /// <param name="edges">The procedure graph's edges.</param>
    public static void RefuseEdgesPointingAtUnknownNodes(
        IReadOnlyList<ProcedureNode> nodes,
        IReadOnlyList<ProcedureEdge> edges)
    {
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            nodeIds.Add(node.Id);
        }

        foreach (var edge in edges)
        {
            if (!nodeIds.Contains(edge.FromNodeId))
            {
                throw new ProcedureLayeringException(
                    ProcedureLayeringException.EdgeTargetsUnknownNode,
                    $"Edge from '{edge.FromNodeId}' ({edge.FromPort}) references an unknown source node id; the procedure graph does not contain it.");
            }

            if (!nodeIds.Contains(edge.ToNodeId))
            {
                throw new ProcedureLayeringException(
                    ProcedureLayeringException.EdgeTargetsUnknownNode,
                    $"Edge to '{edge.ToNodeId}' references an unknown destination node id; the procedure graph does not contain it.");
            }
        }
    }

    /// <summary>Refuses a node whose kind key is outside the merged allowlist.</summary>
    /// <param name="nodes">The procedure graph's nodes.</param>
    /// <param name="allowed">The merged allowlist (platform ∪ project's additions).</param>
    public static void RefuseNodesWithDisallowedKindKeys(
        IReadOnlyList<ProcedureNode> nodes,
        IReadOnlySet<string> allowed)
    {
        foreach (var node in nodes)
        {
            if (!allowed.Contains(node.KindKey))
            {
                throw new ProcedureLayeringException(
                    ProcedureLayeringException.KindNotAllowed,
                    $"Node '{node.Id}' references kind key '{node.KindKey}' which the platform's AllowedKindKeys does not include; the project must declare the kind in the platform's kind catalog before layering can accept it (spec scenario: 'Unknown node kind is rejected').");
            }
        }
    }
}
