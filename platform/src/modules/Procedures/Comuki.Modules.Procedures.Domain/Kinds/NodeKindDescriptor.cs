using Comuki.Modules.Procedures.Domain.Kinds.Types;

namespace Comuki.Modules.Procedures.Domain.Kinds;

/// <summary>
/// Typed descriptor of one node kind: the catalog entry a procedure
/// references by key. Holds everything the compile gate needs to enforce
/// at compile time — outcome ports, parameter schema, evidence
/// requirements, owning surface, policy metadata, and an optional
/// editions feature key (spec requirement: "Typed kind descriptors").
/// Plain <see cref="string"/> is the parameter schema for v1 (the
/// brain-side prompt template name); structural validation lands in task
/// 1.2 when the schema language is decided.
/// </summary>
/// <param name="Key">Stable kind identifier (<c>verify</c>, <c>human-gate</c>, …).</param>
/// <param name="Title">Short display title for Studio and breadcrumbs.</param>
/// <param name="Description">One-line description for catalogs and the compile diagnostic.</param>
/// <param name="OwnerSurface">Where an instance of this kind runs.</param>
/// <param name="ParameterSchema">Reference to the parameter schema (v1: a string name; expanded later).</param>
/// <param name="OutcomePorts">Typed outcome ports — every edge attaches to one (spec §6).</param>
/// <param name="EvidenceRequirements">Evidence types an instance must produce before leaving the kind.</param>
/// <param name="RiskClass">Risk class — policy metadata (low/medium/high/critical).</param>
/// <param name="Idempotency">Whether the kind requires idempotency to be declared at compile time.</param>
/// <param name="ApprovalFloor">Minimum distinct approvers (0, 1, or 2); enforced by the human-gate integration.</param>
/// <param name="EditionsFeatureKey">Optional editions key — present only when the kind is paid-only.</param>
public sealed record NodeKindDescriptor(
    string Key,
    string Title,
    string Description,
    NodeKindOwnerSurface OwnerSurface,
    string ParameterSchema,
    IReadOnlyList<OutcomePort> OutcomePorts,
    IReadOnlyList<string> EvidenceRequirements,
    string RiskClass,
    NodeKindIdempotency Idempotency,
    int ApprovalFloor,
    string? EditionsFeatureKey);
