using Comuki.Modules.Procedures.Application.ProcedureVersions;
using Comuki.Modules.Procedures.Application.ProcedureVersions.Ledger;
using Comuki.Modules.Procedures.Application.Runtime.Trace.Storage;

namespace Comuki.Modules.Procedures.Application.Admission;

/// <summary>The pin recorded when a task is admitted against a procedure.</summary>
/// <param name="RunId">The run the pin was recorded for; the trace endpoint keys
/// the planned-vs-observed trace on this id (spec: "Planned versus observed
/// replay", task 5.3).</param>
/// <param name="VersionId">The content-addressed version id the task pins.</param>
/// <param name="ProcedureKey">The procedure the task rides.</param>
/// <param name="ProjectId">The project that owns the procedure.</param>
/// <param name="PinnedAt">When the pin was recorded (UTC).</param>
public sealed record AdmissionPin(
    Guid RunId,
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
    /// Resolves the latest published version for the procedure, records the
    /// pin in the attempt ledger, and seeds the run's planned-vs-observed
    /// trace with a <c>pin_recorded</c> event. Returns null when the project
    /// has no published procedure with the given key.
    /// </summary>
    /// <param name="runId">The run this pin belongs to — keys the planned-vs-observed trace.</param>
    /// <param name="projectId">The project whose procedure to bind.</param>
    /// <param name="procedureKey">Stable key identifying the procedure.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public Task<AdmissionPin?> TryBindAsync(
        Guid runId,
        Guid projectId,
        string procedureKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default binder: resolves the latest version via the attempt-pin resolver,
/// records the pin in the ledger, seeds the run's trace store, and returns the
/// admission pin. Purely additive — a null return means the task runs unpinned.
/// </summary>
/// <param name="pinResolver">Returns the latest published version.</param>
/// <param name="pinLedger">Records the from/to version transition.</param>
/// <param name="traceStore">Seeds the planned-vs-observed trace at admission.</param>
/// <param name="clock">Time source.</param>
public sealed class ProcedureAdmissionBinder(
    IAttemptPinResolver pinResolver,
    IAttemptPinLedger pinLedger,
    IProcedureTraceStore traceStore,
    TimeProvider clock) : IAdmissionBinder
{
    /// <inheritdoc />
    public async Task<AdmissionPin?> TryBindAsync(
        Guid runId,
        Guid projectId,
        string procedureKey,
        CancellationToken cancellationToken = default)
    {
        var versionId = await pinResolver.ResolveCurrentVersionIdAsync(projectId, procedureKey, cancellationToken);
        if (versionId is null)
        {
            return null;
        }

        var pinnedAt = clock.GetUtcNow();

        await pinLedger.RecordAsync(
            new AttemptVersionTransition(
                AttemptId: Guid.Empty,
                ProjectId: projectId,
                ProcedureKey: procedureKey,
                FromVersionId: null,
                ToVersionId: versionId,
                RecordedAt: pinnedAt),
            cancellationToken);

        // Seed the run's planned-vs-observed trace with the admission
        // pin. ProcedureTraceRecorder records a synthetic "pin_recorded"
        // event on the seed; later events (repair generation opened/closed,
        // human gate resolved, late result, …) append immutably via
        // IProcedureTraceStore.RecordAsync.
        await traceStore.SeedAsync(
            runId,
            versionId,
            procedureKey,
            projectId,
            pinnedAt,
            cancellationToken);

        return new AdmissionPin(runId, versionId, procedureKey, projectId, pinnedAt);
    }
}
