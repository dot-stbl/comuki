using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Comuki.Modules.Procedures.Domain.Patches.Model;

namespace Comuki.Modules.Procedures.Domain.Patches.Publication;

/// <summary>
/// Refuses a patch that tries to widen its own surface before it
/// reaches the compile gate (design decision 5: "patches touching
/// publish rights, autonomy ceilings, or budget maxima are refused
/// before compile"). Pure function over the patch and the procedure's
/// policy context — no I/O, no DI.
/// 
/// <para>
/// Three surfaces the validator checks, each producing a typed
/// <see cref="PublicationException"/> with the offending element named:
/// <list type="number">
///   <item><b>Kind allowlist</b> — an <c>AddNode</c> op whose kind key
///   is absent from <see cref="PublicationContext.AllowedKindKeys"/>
///   is the patch trying to introduce a kind the platform never
///   authorized this procedure to instance (which is how a brain
///   would try to widen its own surface via a fresh paid kind).</item>
///   <item><b>Autonomy ceiling</b> — an <c>AddNode</c> or
///   <c>ReParameterizeNode</c> op on a <c>human-gate</c> kind whose
///   <c>approvals</c> parameter is below
///   <see cref="PublicationContext.MinApprovals"/> is the patch
///   trying to lower the approval count below the floor.</item>
///   <item><b>Budget maximum</b> — an <c>AddNode</c> or
///   <c>ReParameterizeNode</c> op on a <c>repair-boundary</c> kind
///   whose <c>max-generations</c> parameter exceeds
///   <see cref="PublicationContext.MaxGenerations"/> is the patch
///   trying to widen the budget cap above the floor.</item>
/// </list>
/// </para>
/// 
/// <para>
/// <b>Publish rights</b> (the third verb in design decision 5) are
/// checked at the publication-service layer, not here — the brain
/// cannot publish its own draft regardless of the patch's contents.
/// The validator handles graph-shape policy; the publication service
/// handles identity policy.
/// </para>
/// </summary>
public static class PublicationSurfaceValidator
{
    /// <summary>Stable parameter name the compile gate reads for human-gate approvals.</summary>
    public const string ApprovalsParameter = "approvals";

    /// <summary>Stable parameter name the compile gate reads for repair-boundary generations.</summary>
    public const string MaxGenerationsParameter = "max-generations";

    /// <summary>Catalog key the compile gate recognises for a human-gate node.</summary>
    public const string HumanGateKind = "human-gate";

    /// <summary>Catalog key the compile gate recognises for a repair-boundary node.</summary>
    public const string RepairBoundaryKind = "repair-boundary";

    /// <summary>
    /// Validates that <paramref name="patch"/> does not widen its own
    /// surface against the procedure's policy
    /// <paramref name="context"/>. Throws <see cref="PublicationException"/>
    /// on the first violation; the validator does not collect a list
    /// because the publication is rejected on the first refusal anyway.
    /// </summary>
    /// <param name="baseGraph">The graph the patch was drafted against (used to look up existing nodes for re-parameterize checks).</param>
    /// <param name="patch">The patch to validate.</param>
    /// <param name="context">The procedure's effective policy context (allowed kinds + floors).</param>
    public static void Validate(
        ProcedureGraph baseGraph,
        GraphPatch patch,
        PublicationContext context)
    {
        var presentNodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in baseGraph.Nodes)
        {
            presentNodeIds.Add(node.Id);
        }

        var presentNodes = new Dictionary<string, ProcedureNode>(baseGraph.Nodes.Count, StringComparer.Ordinal);
        foreach (var node in baseGraph.Nodes)
        {
            presentNodes[node.Id] = node;
        }

        foreach (var operation in patch.Operations)
        {
            switch (operation)
            {
                case GraphPatchOperation.AddNode add:
                    CheckKindAllowed(add.Node.KindKey, add.Node.Id, context);
                    CheckPolicyParameters(add.Node.KindKey, add.Node.Id, add.Node.Parameters, context);
                    presentNodeIds.Add(add.Node.Id);
                    presentNodes[add.Node.Id] = add.Node;
                    break;
                case GraphPatchOperation.ReParameterizeNode reparam:
                    if (!presentNodes.TryGetValue(reparam.NodeId, out var existingNode))
                    {
                        break;
                    }

                    CheckPolicyParameters(existingNode.KindKey, existingNode.Id, reparam.NewParameters, context);
                    presentNodes[reparam.NodeId] = existingNode with
                    {
                        Parameters = reparam.NewParameters,
                    };
                    break;
                case GraphPatchOperation.RemoveNode:
                case GraphPatchOperation.RewireEdge:
                    break;
                default:
                    throw new PublicationException(
                        PublicationException.KindNotAllowed,
                        $"Unknown GraphPatchOperation type '{operation.GetType().FullName}'; the closed hierarchy should reject this at compile time.");
            }
        }
    }

    /// <summary>Refuses an added node whose kind is outside the procedure's merged allowlist.</summary>
    /// <param name="kindKey">The added node's kind key.</param>
    /// <param name="nodeId">The added node's graph-local id.</param>
    /// <param name="context">The publication context carrying the allowlist.</param>
    public static void CheckKindAllowed(string kindKey, string nodeId, PublicationContext context)
    {
        if (!context.AllowedKindKeys.Contains(kindKey))
        {
            throw new PublicationException(
                PublicationException.KindNotAllowed,
                $"AddNode: node '{nodeId}' references kind '{kindKey}' which is not in the procedure's AllowedKindKeys; the patch may not introduce kinds outside the platform's grant (design decision 5).");
        }
    }

    /// <summary>
    /// Refuses policy nodes whose autonomy parameters fall below the
    /// procedure's floors — a human gate below <c>MinApprovals</c> or a
    /// repair boundary beyond <c>MaxGenerations</c> (design decision 5).
    /// </summary>
    /// <param name="kindKey">The added node's kind key.</param>
    /// <param name="nodeId">The added node's graph-local id.</param>
    /// <param name="parameters">The added node's parameter map.</param>
    /// <param name="context">The publication context carrying the floors.</param>
    public static void CheckPolicyParameters(
        string kindKey,
        string nodeId,
        IReadOnlyDictionary<string, string> parameters,
        PublicationContext context)
    {
        if (string.Equals(kindKey, HumanGateKind, StringComparison.Ordinal))
        {
            if (!parameters.TryGetValue(ApprovalsParameter, out var approvalsText)
                || !int.TryParse(approvalsText, out var approvals))
            {
                throw new PublicationException(
                    PublicationException.PolicyParameterMissing,
                    $"Human-gate node '{nodeId}' is missing the '{ApprovalsParameter}' parameter, or it is not an integer; the patch may not introduce a human gate whose approval count is undefined.");
            }

            if (approvals < context.MinApprovals)
            {
                throw new PublicationException(
                    PublicationException.AutonomyBelowFloor,
                    $"Human-gate node '{nodeId}' declares approvals={approvals}; the procedure's MinApprovals floor is {context.MinApprovals}. The patch may not lower the autonomy ceiling (design decision 5).");
            }
        }
        else if (string.Equals(kindKey, RepairBoundaryKind, StringComparison.Ordinal))
        {
            if (!parameters.TryGetValue(MaxGenerationsParameter, out var generationsText)
                || !int.TryParse(generationsText, out var generations))
            {
                throw new PublicationException(
                    PublicationException.PolicyParameterMissing,
                    $"Repair-boundary node '{nodeId}' is missing the '{MaxGenerationsParameter}' parameter, or it is not an integer; the patch may not introduce a repair boundary whose generation cap is undefined.");
            }

            if (generations > context.MaxGenerations)
            {
                throw new PublicationException(
                    PublicationException.BudgetAboveFloor,
                    $"Repair-boundary node '{nodeId}' declares max-generations={generations}; the procedure's MaxGenerations floor is {context.MaxGenerations}. The patch may not widen the budget maximum (design decision 5).");
            }
        }
    }
}
