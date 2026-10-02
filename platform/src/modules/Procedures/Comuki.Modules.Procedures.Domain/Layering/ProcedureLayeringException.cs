using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Procedures.Domain.Layering;

/// <summary>
/// Domain exception thrown when the layered-procedure merge refuses
/// input — duplicate node ids, edges pointing at absent nodes,
/// repositories trying to add control flow, or a project's
/// <see cref="Definitions.ProcedureDefinition"/> using a kind outside
/// the platform's <see cref="Model.PlatformDefaults.AllowedKindKeys"/>.
/// Conflicts are errors, not last-writer-wins (design decision 2). Maps
/// to HTTP 422 via the shared <see cref="DomainException"/> handler —
/// semantic failure, not an upstream 502.
/// </summary>
/// <param name="code">Stable dot.case identifier.</param>
/// <param name="message">Safe human message — names the offending element.</param>
public sealed class ProcedureLayeringException(string code, string message)
    : DomainException(code, message)
{
    /// <summary>A procedure definition has two nodes with the same id (the layering surfaces the id and node kind).</summary>
    public const string DuplicateNodeId = "procedures.layering.duplicate_node_id";

    /// <summary>An edge references a node id that the definition does not contain.</summary>
    public const string EdgeTargetsUnknownNode = "procedures.layering.edge_targets_unknown_node";

    /// <summary>A node references a kind key the platform's <see cref="Model.PlatformDefaults"/> does not allow.</summary>
    public const string KindNotAllowed = "procedures.layering.kind_not_allowed";

    /// <summary>A repository binding tries to introduce nodes or edges — bindings may only select.</summary>
    public const string RepositoryBindingAddsControlFlow = "procedures.layering.repository_binding_adds_control_flow";
}
