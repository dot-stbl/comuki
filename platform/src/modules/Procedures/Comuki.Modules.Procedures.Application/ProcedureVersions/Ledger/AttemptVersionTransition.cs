namespace Comuki.Modules.Procedures.Application.ProcedureVersions.Ledger;

/// <summary>
/// One entry in the attempt-pin ledger: a single transition from
/// one version pin to another, recorded when a new attempt is
/// admitted (task 3.3 spec scenario: "the attempt ledger records the
/// version change between attempts"). Replay's planned-vs-observed
/// trace (task 5.3) reads this ledger to answer "why did attempt 3
/// see a different procedure than attempt 2?" — the ledger is the
/// audit log of procedure-version transitions at the attempt
/// boundary, distinct from the publication-side history in
/// <c>CompiledProcedureVersion</c>.
/// 
/// <para>
/// The first attempt has no previous pin; the ledger records the
/// transition with <see cref="FromVersionId"/> set to null so the
/// replay classifier can distinguish "the procedure changed" from
/// "the attempt is the first under the current version".
/// </para>
/// </summary>
/// <param name="AttemptId">The attempt that the new pin belongs to (the runs layer's id).</param>
/// <param name="ProjectId">The project the procedure belongs to.</param>
/// <param name="ProcedureKey">The procedure's stable key inside the project.</param>
/// <param name="FromVersionId">The content-addressed id the previous attempt pinned (null for the first attempt).</param>
/// <param name="ToVersionId">The content-addressed id the new attempt pins.</param>
/// <param name="RecordedAt">When the transition was recorded (UTC).</param>
public sealed record AttemptVersionTransition(
    Guid AttemptId,
    Guid ProjectId,
    string ProcedureKey,
    string? FromVersionId,
    string ToVersionId,
    DateTimeOffset RecordedAt);
