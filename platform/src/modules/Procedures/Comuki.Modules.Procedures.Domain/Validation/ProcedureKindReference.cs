namespace Comuki.Modules.Procedures.Domain.Validation;

/// <summary>
/// Reference to a published procedure that depends on one or more node
/// kinds. Returned by <see cref="Ports.IProcedureKindUsageLookup"/>; the catalog
/// validator uses this list to refuse retraction when any procedure still
/// depends on the kind (spec scenario: "Retraction blocked by reference").
/// Pure data — no behaviour, no I/O.
/// </summary>
/// <param name="ProcedureId">Stable identifier of the published procedure.</param>
/// <param name="ProcedureName">Human-readable procedure name (used in refusal messages).</param>
public sealed record ProcedureKindReference(string ProcedureId, string ProcedureName);
