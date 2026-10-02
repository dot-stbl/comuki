using Comuki.Modules.Procedures.Application.ProcedureVersions.Ledger;
namespace Comuki.Modules.Procedures.Application.ProcedureVersions;

/// <summary>
/// Application-layer seam the orchestration / runs layer calls at
/// retry time (task 3.3: "a new attempt pins the then-current
/// version"). Given a (project, procedureKey) pair, returns the
/// content-addressed id of the most recently published version — the
/// one a fresh attempt should pin against. The retry ledger (see
/// <see cref="IAttemptPinLedger"/>) records the transition from the
/// previous pin so a planned-vs-observed trace can show "attempt 3
/// pinned v5 after attempt 2 was approved under v4".
/// 
/// <para>
/// The resolver is the only place this knowledge lives: the runs
/// layer does not need to know the procedure's history beyond
/// "what is current" — the rest of the audit trail is recorded
/// ledger-side and read by replay (task 5.3).
/// </para>
/// </summary>
public interface IAttemptPinResolver
{
    /// <summary>
    /// Returns the content-addressed id of the most recently
    /// published version for the (project, procedureKey) pair, or
    /// <c>null</c> when no version has been published yet. Callers
    /// that need a guarantee (a non-null id) should refuse a retry
    /// when the resolver returns null — a procedure with no
    /// published version has nothing to pin.
    /// </summary>
    /// <param name="projectId">The project the procedure belongs to.</param>
    /// <param name="procedureKey">The procedure's stable key inside the project.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<string?> ResolveCurrentVersionIdAsync(
        Guid projectId,
        string procedureKey,
        CancellationToken cancellationToken = default);
}
