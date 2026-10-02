using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Procedures.Domain.Patches.Diff;

/// <summary>
/// Typed refusal from the <see cref="GraphPatchValidator"/> or the
/// <see cref="GraphPatchApplier"/>. Stable codes map to distinct
/// operator actions; the message always names the offending element
/// (node id, edge tuple, parameter key) so the Studio diff and the
/// chat reply can surface the reason without a re-run. Maps to HTTP
/// 422 via the shared <see cref="DomainException"/> handler — semantic
/// failure of the proposal object, not an upstream 502.
/// </summary>
/// <param name="code">Stable dot.case identifier.</param>
/// <param name="message">Safe human message — names the offending element.</param>
public sealed class GraphPatchException(string code, string message)
    : DomainException(code, message)
{
    /// <summary>
    /// An <c>AddNode</c> op references an id that already exists in the
    /// base graph (or was added by an earlier op in the same patch).
    /// </summary>
    public const string NodeAlreadyExists = "procedures.patch.node_already_exists";

    /// <summary>
    /// A <c>RemoveNode</c> or <c>ReParameterizeNode</c> op references an
    /// id absent from the base graph (and not produced by an earlier
    /// <c>AddNode</c> op in the same patch).
    /// </summary>
    public const string NodeNotFound = "procedures.patch.node_not_found";

    /// <summary>
    /// A <c>RewireEdge</c> op's <c>Before</c> edge does not exist in the
    /// base graph (or in the post-op state the patch has built so far).
    /// </summary>
    public const string EdgeNotFound = "procedures.patch.edge_not_found";

    /// <summary>
    /// A <c>RewireEdge</c> op's <c>After</c> edge collides with an edge
    /// already present in the post-op state — wires must remain unique
    /// per (from, port, to) tuple.
    /// </summary>
    public const string EdgeAlreadyExists = "procedures.patch.edge_already_exists";

    /// <summary>
    /// Two ops in the same patch conflict (e.g. <c>RemoveNode "x"</c>
    /// followed by <c>RewireEdge</c> whose <c>Before</c> targets <c>x</c>).
    /// The validator catches these before application to avoid surprising
    /// mid-flight failures.
    /// </summary>
    public const string ConflictingOperations = "procedures.patch.conflicting_operations";

    /// <summary>
    /// The patch's <c>BaseVersionId</c> does not match any compiled
    /// version for the (project, procedureKey) pair (set in the
    /// Application layer; surfaced here so the same exception type
    /// covers both the diff path and the apply path).
    /// </summary>
    public const string InvalidBaseVersion = "procedures.patch.invalid_base_version";

    /// <summary>
    /// The patch carries a forbidden surface — a node whose kind key is
    /// absent from the platform's <c>AllowedKindKeys</c>, an edge to a
    /// node the platform's allowlist forbids, or a parameter that
    /// contradicts the procedure's <c>MinApprovals</c> /
    /// <c>MaxGenerations</c> floors. The compile gate widens this check
    /// at task 3.2; the validator at task 3.1 catches the obvious
    /// "patch tries to widen its own scope" cases (spec scenario:
    /// "Patch touches forbidden surface").
    /// </summary>
    public const string ForbiddenSurface = "procedures.patch.forbidden_surface";
}
