namespace Comuki.Modules.Procedures.Domain.Catalog;

/// <summary>
/// Frontmatter-style parsed view of one procedure-node-kind markdown file:
/// the stable key, the metadata the catalog indexes, and the unparsed body
/// (future-proof: brains and docs read it). Pure data — produced by the
/// parser, consumed by the reader and the catalog validator. No I/O, no
/// domain references (this is the wire shape, not the domain shape).
/// </summary>
/// <param name="Key">File-stem key (the catalog-stable identifier).</param>
/// <param name="Title">Short display title.</param>
/// <param name="Description">One-line description for catalogs and the compile diagnostic.</param>
/// <param name="OwnerSurface">Wire form of the owner surface.</param>
/// <param name="ParameterSchema">Parameter schema reference (v1: a string name).</param>
/// <param name="OutcomePorts">Wire-form port names declared by this kind.</param>
/// <param name="EvidenceRequirements">Evidence types an instance must produce.</param>
/// <param name="RiskClass">Risk class — policy metadata (low/medium/high/critical).</param>
/// <param name="Idempotency">Wire form of <see cref="Kinds.Types.NodeKindIdempotency"/>.</param>
/// <param name="ApprovalFloor">Minimum distinct approvers (0, 1, or 2).</param>
/// <param name="EditionsFeatureKey">Optional editions key — present only when the kind is paid-only.</param>
/// <param name="Body">Markdown body after the closing fence.</param>
public sealed record NodeKindDescriptorDocument(
    string Key,
    string Title,
    string Description,
    string OwnerSurface,
    string ParameterSchema,
    IReadOnlyList<string> OutcomePorts,
    IReadOnlyList<string> EvidenceRequirements,
    string RiskClass,
    string Idempotency,
    int ApprovalFloor,
    string? EditionsFeatureKey,
    string Body);
