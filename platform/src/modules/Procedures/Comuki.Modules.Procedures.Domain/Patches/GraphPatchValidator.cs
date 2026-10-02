using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Comuki.Modules.Procedures.Domain.Patches.Diff;
using Comuki.Modules.Procedures.Domain.Patches.Model;

namespace Comuki.Modules.Procedures.Domain.Patches;

/// <summary>
/// Validates that a <see cref="GraphPatch"/> can be applied to its base
/// <see cref="ProcedureGraph"/> without surprising the caller mid-flight.
/// Pure — no I/O, no DI. The validator runs once at draft time and again
/// at publication (task 3.2) so a stale draft that drifted from its base
/// is refused before reaching the compile gate.
/// 
/// <para>
/// What the validator catches (every refusal names the offending element):
/// <list type="number">
///   <item><c>AddNode</c> collisions — the new id is already in the base
///   or was added by an earlier op in the same patch.</item>
///   <item><c>RemoveNode</c> / <c>ReParameterizeNode</c> on ids absent
///   from the base and not produced by an earlier <c>AddNode</c>.</item>
///   <item><c>RewireEdge</c> whose <c>Before</c> does not exist in the
///   post-op state, or whose <c>After</c> collides with an existing
///   wire.</item>
///   <item>Self-referential patches — <c>AddNode x</c> followed by
///   <c>RemoveNode x</c> for the same id is allowed (net no-op) but the
///   later op still needs to find the earlier op's node; the validator
///   tracks added ids so <c>RemoveNode x</c> succeeds only when <c>x</c>
///   was either in base or added earlier in the patch.</item>
/// </list>
/// </para>
/// 
/// <para>
/// Forbidden-surface checks (publish rights, autonomy ceilings, budget
/// maxima — design decision 5) belong to the compile gate at task 3.2;
/// the validator at task 3.1 stays at graph-shape invariants so it can
/// be unit-tested without a catalog or an editions source.
/// </para>
/// </summary>
public static class GraphPatchValidator
{
    /// <summary>
    /// Validates <paramref name="patch"/> against <paramref name="baseGraph"/>.
    /// Throws <see cref="GraphPatchException"/> with a stable code and a
    /// message that names the offending element on the first invariant
    /// violation; the validator does not collect a list of all violations
    /// because the patch is rejected on the first failure anyway.
    /// </summary>
    /// <param name="baseGraph">The graph the patch was drafted against.</param>
    /// <param name="patch">The patch to validate.</param>
    public static void Validate(ProcedureGraph baseGraph, GraphPatch patch)
    {
        var presentNodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in baseGraph.Nodes)
        {
            presentNodeIds.Add(node.Id);
        }

        var presentEdges = new HashSet<ProcedureEdge>(baseGraph.Edges);

        foreach (var operation in patch.Operations)
        {
            switch (operation)
            {
                case GraphPatchOperation.AddNode add:
                    if (!presentNodeIds.Add(add.Node.Id))
                    {
                        throw new GraphPatchException(
                            GraphPatchException.NodeAlreadyExists,
                            $"AddNode: node id '{add.Node.Id}' already exists in the base graph (or was added by an earlier op in this patch).");
                    }

                    break;
                case GraphPatchOperation.RemoveNode remove:
                    if (!presentNodeIds.Contains(remove.NodeId))
                    {
                        throw new GraphPatchException(
                            GraphPatchException.NodeNotFound,
                            $"RemoveNode: node id '{remove.NodeId}' is not in the base graph and was not added by an earlier op in this patch.");
                    }

                    presentNodeIds.Remove(remove.NodeId);
                    presentEdges.RemoveWhere(edge =>
                        string.Equals(edge.FromNodeId, remove.NodeId, StringComparison.Ordinal)
                        || string.Equals(edge.ToNodeId, remove.NodeId, StringComparison.Ordinal));
                    break;
                case GraphPatchOperation.RewireEdge rewire:
                    if (!presentEdges.Contains(rewire.Before))
                    {
                        throw new GraphPatchException(
                            GraphPatchException.EdgeNotFound,
                            $"RewireEdge: edge ({rewire.Before.FromNodeId} --{rewire.Before.FromPort}--> {rewire.Before.ToNodeId}) is not in the base graph (or has already been rewired by an earlier op in this patch).");
                    }

                    presentEdges.Remove(rewire.Before);

                    if (rewire.Before.Equals(rewire.After))
                    {
                        break;
                    }

                    if (!presentEdges.Add(rewire.After))
                    {
                        throw new GraphPatchException(
                            GraphPatchException.EdgeAlreadyExists,
                            $"RewireEdge: edge ({rewire.After.FromNodeId} --{rewire.After.FromPort}--> {rewire.After.ToNodeId}) already exists in the post-op state.");
                    }

                    break;
                case GraphPatchOperation.ReParameterizeNode reparam:
                    if (!presentNodeIds.Contains(reparam.NodeId))
                    {
                        throw new GraphPatchException(
                            GraphPatchException.NodeNotFound,
                            $"ReParameterizeNode: node id '{reparam.NodeId}' is not in the base graph and was not added by an earlier op in this patch.");
                    }

                    break;
                default:
                    throw new GraphPatchException(
                        GraphPatchException.ConflictingOperations,
                        $"Unknown GraphPatchOperation type '{operation.GetType().FullName}'; the closed hierarchy should reject this at compile time.");
            }
        }
    }
}
