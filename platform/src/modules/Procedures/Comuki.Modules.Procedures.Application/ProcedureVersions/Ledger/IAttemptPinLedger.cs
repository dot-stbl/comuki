namespace Comuki.Modules.Procedures.Application.ProcedureVersions.Ledger;

/// <summary>
/// Application-layer seam that records <see cref="AttemptVersionTransition"/>
/// entries at the attempt boundary (task 3.3). The ledger is the
/// audit log of procedure-version transitions for a (project,
/// procedureKey) pair; replay reads it to classify attempt
/// divergence as "within policy" (the procedure did not change
/// between attempts) or "because of a republish" (the procedure
/// changed; the diff is allowed by the policy).
/// 
/// <para>
/// The infrastructure layer (task 5.3, when persistence lands) wires
/// the EF-backed implementation; the in-memory implementation lives
/// in this layer for unit tests and for environments that do not yet
/// have a database. The seam keeps the runs layer from caring which
/// store is wired.
/// </para>
/// </summary>
public interface IAttemptPinLedger
{
    /// <summary>
    /// Records <paramref name="transition"/>. Idempotent on
    /// <see cref="AttemptVersionTransition.AttemptId"/>: a second call
    /// with the same attempt id is a no-op (the first call's pin
    /// wins, subsequent calls cannot rewrite history).
    /// </summary>
    /// <param name="transition">The transition to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RecordAsync(
        AttemptVersionTransition transition,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the ledger entries for the (project, procedureKey)
    /// pair, oldest first. Used by replay (task 5.3) to walk the
    /// version transition history of a procedure.
    /// </summary>
    /// <param name="projectId">The project the procedure belongs to.</param>
    /// <param name="procedureKey">The procedure's stable key inside the project.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IReadOnlyList<AttemptVersionTransition>> ListAsync(
        Guid projectId,
        string procedureKey,
        CancellationToken cancellationToken = default);
}
