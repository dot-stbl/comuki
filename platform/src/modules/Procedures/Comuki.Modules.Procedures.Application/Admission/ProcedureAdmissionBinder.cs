using Comuki.Modules.Procedures.Application.ProcedureVersions;
using Comuki.Modules.Procedures.Application.ProcedureVersions.Ledger;

namespace Comuki.Modules.Procedures.Application.Admission;

/// <summary>The pin recorded when a task is admitted against a procedure.</summary>
/// <param name="VersionId">The content-addressed version id the task pins.</param>
/// <param name="ProcedureKey">The procedure the task rides.</param>
/// <param name="ProjectId">The project that owns the procedure.</param>
/// <param name="PinnedAt">When the pin was recorded (UTC).</param>
public sealed record AdmissionPin(
    string VersionId,
    string ProcedureKey,
    Guid ProjectId,
    DateTimeOffset PinnedAt);

/// <summary>
/// Port the admission path calls when deciding whether a task should
/// ride a procedure. Returns null when no procedure is configured —
/// the unpinned default remains (spec: "procedure-pinned materialization
/// is additive").
/// </summary>
public interface IAdmissionBinder
{
    /// <summary>
    /// Resolves the latest published version for the procedure and records
    /// the pin in the attempt ledger. Returns null when the project has no
    /// published procedure with the given key.
    /// </summary>
    /// <param name="projectId">The project whose procedure to bind.</param>
    /// <param name="procedureKey">Stable key identifying the procedure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<AdmissionPin?> TryBindAsync(
        Guid projectId,
        string procedureKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default binder: resolves the latest version via the attempt-pin resolver,
/// records the pin in the ledger, returns the admission pin. Purely additive —
/// a null return means the task runs unpinned.
/// </summary>
/// <param name="pinResolver">Returns the latest published version.</param>
/// <param name="pinLedger">Records the from/to version transition.</param>
/// <param name="clock">Time source.</param>
public sealed class ProcedureAdmissionBinder(
    IAttemptPinResolver pinResolver,
    IAttemptPinLedger pinLedger,
    TimeProvider clock) : IAdmissionBinder
{
    /// <inheritdoc />
    public async Task<AdmissionPin?> TryBindAsync(
        Guid projectId,
        string procedureKey,
        CancellationToken cancellationToken = default)
    {
        var versionId = await pinResolver.ResolveCurrentVersionIdAsync(projectId, procedureKey, cancellationToken);
        if (versionId is null)
        {
            return null;
        }

        await pinLedger.RecordAsync(
            new AttemptVersionTransition(
                AttemptId: Guid.Empty,
                ProjectId: projectId,
                ProcedureKey: procedureKey,
                FromVersionId: null,
                ToVersionId: versionId,
                RecordedAt: clock.GetUtcNow()),
            cancellationToken);

        return new AdmissionPin(versionId, procedureKey, projectId, clock.GetUtcNow());
    }
}
