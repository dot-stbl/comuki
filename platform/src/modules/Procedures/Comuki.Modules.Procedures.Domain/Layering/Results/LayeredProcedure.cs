using Comuki.Modules.Procedures.Domain.Definitions.Elements;

namespace Comuki.Modules.Procedures.Domain.Layering.Results;

/// <summary>
/// Result of a layered-procedure merge. Carries the resolved graph, the
/// merged allowlist, the effective floors, and the repository bindings
/// the procedure is published against. The compile gate (task 2.3)
/// reads the <see cref="Definitions.ProcedureDefinition"/>-shaped graph through
/// this record; one procedure may back several repository bindings,
/// each resolved against the same compiled version (spec requirement:
/// "pinned version is byte-identical on re-read").
/// </summary>
/// <param name="ProcedureName">The project's procedure name — the merge is keyed on it.</param>
/// <param name="Nodes">The graph's nodes (project's, validated against the platform's allowlist).</param>
/// <param name="Edges">The graph's typed-port edges.</param>
/// <param name="AllowedKindKeys">The merged allowlist (platform ∪ project's additional keys).</param>
/// <param name="MaxGenerations">The effective repair-boundary generations floor.</param>
/// <param name="MinApprovals">The effective human-gate approval floor.</param>
/// <param name="RepositoryBindings">
/// The repository bindings merged into this procedure. Empty when
/// the procedure is published but no repository has bound yet —
/// the binding happens out-of-band once the procedure is committed.
/// </param>
public sealed record LayeredProcedure(
    string ProcedureName,
    IReadOnlyList<ProcedureNode> Nodes,
    IReadOnlyList<ProcedureEdge> Edges,
    IReadOnlySet<string> AllowedKindKeys,
    int MaxGenerations,
    int MinApprovals,
    IReadOnlyList<Model.RepositoryBinding> RepositoryBindings);
