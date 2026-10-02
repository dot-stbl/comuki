using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Procedures.Domain.Exceptions;

/// <summary>
/// Semantic invariant violation inside the Procedures (node-kinds) catalog —
/// covers the validation and versioning requirements: descriptor schema
/// violations (missing owner surface, blank ports, duplicate keys, …) and
/// retraction refused because a published procedure still depends on the
/// kind (spec scenario: "Retraction blocked by reference"). Maps to HTTP
/// 422 via the shared <see cref="DomainException"/> handler.
/// </summary>
/// <param name="code">Stable dot.case identifier.</param>
/// <param name="message">Safe human message — no PII, no secrets.</param>
public sealed class ProcedureNodeKindsDomainException(string code, string message)
    : DomainException(code, message)
{
    /// <summary>Stable code for a descriptor missing the owning execution surface.</summary>
    public const string OwnerSurfaceMissing = "procedures.node_kinds.owner_surface_missing";

    /// <summary>Stable code for a duplicate descriptor key in the same catalog.</summary>
    public const string DuplicateKey = "procedures.node_kinds.duplicate_key";

    /// <summary>Stable code for a descriptor schema violation (blank port, duplicate port, blank evidence, etc.).</summary>
    public const string SchemaViolation = "procedures.node_kinds.schema_violation";

    /// <summary>Stable code for retracting a kind still referenced by a published procedure (spec scenario: "Retraction blocked by reference").</summary>
    public const string RetractionBlocked = "procedures.node_kinds.retraction_blocked";

    /// <summary>Stable code for a kind's editions feature key not being granted by the effective edition (spec scenario: "Two-approval gate on community").</summary>
    public const string FeatureKeyNotGranted = "procedures.node_kinds.feature_key_not_granted";
}
