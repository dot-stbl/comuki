using Comuki.Engine.Orchestration.Domain.Exceptions;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Domain.Runs;

/// <summary>
/// Run aggregate — one goal from integrations (ticket / chat) decomposed by the
/// brain into a plan of work items. Ids are UUIDv7 generated client-side;
/// status transitions are guarded by <see cref="RunTransitions"/>.
/// </summary>
public sealed class Run
{
    internal Run()
    {
    }

    /// <summary>Strong-typed run id (UUIDv7).</summary>
    public RunId Id { get; private set; }

    /// <summary>Project scope the run belongs to.</summary>
    public ProjectId ProjectId { get; private set; }

    /// <summary>Current lifecycle status; mutated only via <see cref="TransitionTo"/>.</summary>
    public RunStatus Status { get; private set; }

    /// <summary>When the run was admitted into the queue.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last status change timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Autonomy trust class for this run — <see cref="RunTrustClass.Supervised"/>
    /// by default. Mutated only via <see cref="PromoteTo"/> /
    /// <see cref="DemoteTo"/>. Starts at <see cref="RunTrustClass.Supervised"/>
    /// for every new run regardless of project-level defaults; per-run trust is
    /// the conservative choice that the ratchet then promotes.
    /// </summary>
    public RunTrustClass TrustClass { get; private set; }

    /// <summary>Execution generation — starts at 1; bumped on cancel/supersede to fence stale live executions (see WS5).</summary>
    public int Generation { get; private set; }

    /// <summary>
    /// The WS9 admission idempotency key — the inbox <c>message_id</c> the
    /// launcher claimed before creating this run, so a losing /
    /// retried admission call can look up the winner's run. Null for runs
    /// created any other way (chat / scheduler); set once at
    /// <see cref="Create"/> and never mutated after.
    /// </summary>
    public string? AdmissionMessageId { get; private set; }

    /// <summary>
    /// The Work <c>WorkTaskId</c> that owns this Run, or null until the
    /// Work bridge calls <see cref="StampWorkBacklink"/>. The work-bridge
    /// is the only legal setter of this field — chat / scheduler runs
    /// leave it null and never call <c>StampWorkBacklink</c>.
    /// </summary>
    public Guid? TaskId { get; private set; }

    /// <summary>
    /// The Work-side attempt ordinal this Run corresponds to. The DB
    /// column has a <c>DEFAULT 1</c> for chat / scheduler runs that
    /// never call <see cref="StampWorkBacklink"/>; once the bridge
    /// stamps the backlink, the field is the bridge's source of truth
    /// and a re-stamp with a different ordinal is rejected
    /// (<see cref="OrchestrationErrorCodes.RunWorkBacklinkMismatch"/>).
    /// </summary>
    public int AttemptOrdinal { get; private set; } = 1;

    /// <summary>
    /// The Run id of the previous attempt on the same Task, when this
    /// Run is an attempt N &gt; 1. Null for the first attempt and for
    /// runs that never call <see cref="StampWorkBacklink"/>. The
    /// lineage chain lives in this column; the WS5 cancel/supersede
    /// logic uses it to fence stale live executions.
    /// </summary>
    public Guid? PredecessorRunId { get; private set; }

    /// <summary>
    /// Identifier of the actor that triggered the dispatch (e.g.
    /// <c>integrations/github-bot</c>, <c>scheduler/cron-job-3</c>).
    /// Null until <see cref="StampWorkBacklink"/> stamps it. Bounded
    /// to 128 chars by the DB column.
    /// </summary>
    public string? TriggeringActorId { get; private set; }

    /// <summary>Creates a run in <see cref="RunStatus.Queued"/> — the only legal entry status.</summary>
    /// <param name="projectId"></param>
    /// <param name="now"></param>
    /// <param name="admissionMessageId">
    /// Optional WS9 admission idempotency key (the inbox <c>message_id</c>
    /// the launcher claimed). Set by <c>IntegrationRunLauncher.LaunchAsync</c>
    /// after a successful <see cref="Infrastructure.Inbox.IInbox.TryClaimAsync"/>
    /// so a losing / retried caller can find the winner's run. Null for
    /// every other launcher (chat / scheduler).
    /// </param>
    public static Run Create(ProjectId projectId, DateTimeOffset now, string? admissionMessageId = null)
    {
        return new Run
        {
            Id = RunId.New(),
            ProjectId = projectId,
            Status = RunStatus.Queued,
            TrustClass = RunTrustClass.Supervised,
            Generation = 1,
            AdmissionMessageId = admissionMessageId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Applies a status transition; illegal transitions throw — see <see cref="RunTransitions"/>.</summary>
    /// <param name="to"></param>
    /// <param name="now"></param>
    /// <exception cref="OrchestrationDomainException">the transition is not in <see cref="RunTransitions"/>.</exception>
    public void TransitionTo(RunStatus to, DateTimeOffset now)
    {
        if (!RunTransitions.IsLegal(Status, to))
        {
            throw new OrchestrationDomainException(
                OrchestrationErrorCodes.RunIllegalTransition,
                $"illegal run transition {Status} -> {to}");
        }

        Status = to;
        UpdatedAt = now;
    }

    /// <summary>
    /// Promotes the trust class one rung up the
    /// <see cref="RunTrustClass"/> ladder — <see cref="RunTrustClass.Supervised"/>
    /// to <see cref="RunTrustClass.Pilot"/>, or <see cref="RunTrustClass.Pilot"/>
    /// to <see cref="RunTrustClass.Trusted"/>. Calls at <see cref="RunTrustClass.Trusted"/>
    /// are no-ops (already at the top rung). Changes update
    /// <see cref="UpdatedAt"/>.
    /// </summary>
    /// <param name="now"></param>
    public void PromoteTo(DateTimeOffset now)
    {
        RunTrustClass next;
        if (TrustClass == RunTrustClass.Supervised)
        {
            next = RunTrustClass.Pilot;
        }
        else if (TrustClass == RunTrustClass.Pilot)
        {
            next = RunTrustClass.Trusted;
        }
        else
        {
            return;
        }

        TrustClass = next;
        UpdatedAt = now;
    }

    /// <summary>
    /// Demotes the trust class back to <see cref="RunTrustClass.Supervised"/>.
    /// The ratchet treats every failure as a clean reset — there is no
    /// intermediate <c>Pilot</c>-demote state. Calls when already at
    /// <see cref="RunTrustClass.Supervised"/> are no-ops.
    /// </summary>
    /// <param name="now"></param>
    public void DemoteTo(DateTimeOffset now)
    {
        if (TrustClass == RunTrustClass.Supervised)
        {
            return;
        }

        TrustClass = RunTrustClass.Supervised;
        UpdatedAt = now;
    }

    /// <summary>
    /// Stamps the Work-side backlink on this Run. The Work bridge is
    /// the single caller — it announces which <c>WorkTask</c> owns
    /// this Run and which attempt ordinal it corresponds to. The
    /// stamp is <strong>idempotent on the <c>(taskId, ordinal)</c>
    /// pair</strong>: a second call with the same pair leaves all
    /// four fields untouched and does not advance
    /// <see cref="UpdatedAt"/> (preserves the original stamp time so
    /// audit / line-of-flight analytics don't see a phantom edit).
    /// </summary>
    /// <param name="taskId">The owning WorkTask id.</param>
    /// <param name="ordinal">The attempt ordinal on the WorkTask.</param>
    /// <param name="predecessorRunId">
    /// The previous attempt's Run id; <c>null</c> for the first attempt.
    /// </param>
    /// <param name="triggeringActorId">
    /// The actor that triggered the dispatch
    /// (<c>integrations/github-bot</c>, <c>scheduler/cron-job-3</c>,
    /// …); null/empty is rejected.
    /// </param>
    /// <param name="now">The stamp time.</param>
    /// <exception cref="OrchestrationDomainException">
    /// A subsequent stamp carries a different <c>(taskId, ordinal)</c>
    /// pair — code <see cref="OrchestrationErrorCodes.RunWorkBacklinkMismatch"/>.
    /// </exception>
    public void StampWorkBacklink(
        Guid taskId,
        int ordinal,
        Guid? predecessorRunId,
        string? triggeringActorId,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(triggeringActorId))
        {
            throw new OrchestrationDomainException(
                OrchestrationErrorCodes.RunWorkBacklinkMismatch,
                $"triggering actor id must be a non-empty string on run {Id}");
        }

        if (TaskId is { } existingTask && (existingTask != taskId || AttemptOrdinal != ordinal))
        {
            throw new OrchestrationDomainException(
                OrchestrationErrorCodes.RunWorkBacklinkMismatch,
                $"run {Id} already linked to task {existingTask} attempt {AttemptOrdinal}; " +
                $"second stamp asked for task {taskId} attempt {ordinal}");
        }

        if (TaskId is null)
        {
            TaskId = taskId;
            AttemptOrdinal = ordinal;
            PredecessorRunId = predecessorRunId;
            TriggeringActorId = triggeringActorId;
            UpdatedAt = now;
        }
    }
}
