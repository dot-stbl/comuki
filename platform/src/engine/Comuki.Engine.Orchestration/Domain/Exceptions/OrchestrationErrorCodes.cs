namespace Comuki.Engine.Orchestration.Domain.Exceptions;

/// <summary>
/// Stable machine-readable error codes for engine-domain invariant
/// violations. Clients branch on <c>Code</c>, never on the human
/// <c>Message</c> (mirrors the contract on <see cref="Shared.Kernel.Exceptions.DomainException"/>).
/// Codes follow the <c>orchestration.&lt;aggregate&gt;.&lt;rule&gt;</c>
/// dot.case pattern, OTel-tag friendly (<see cref="OrchestrationDomainException"/>
/// surfaces them as ProblemDetails <c>code</c>).
/// </summary>
public static class OrchestrationErrorCodes
{
    /// <summary>WorkItem factory rejected an empty profile key.</summary>
    public const string WorkItemProfileKeyEmpty = "orchestration.work_item.profile_key.empty";

    /// <summary>WorkItem factory rejected an empty image (worker image + digest).</summary>
    public const string WorkItemImageEmpty = "orchestration.work_item.image.empty";

    /// <summary>WorkItem factory rejected an empty profiles git ref.</summary>
    public const string WorkItemProfilesRefEmpty = "orchestration.work_item.profiles_ref.empty";

    /// <summary>
    /// WorkItem factory rejected an empty environment class
    /// (<c>add-worker-environments</c>, task 3.1) — every implement item
    /// MUST carry the class bound at enqueue; an empty value never matches
    /// a claimer (queue SQL filters <c>env_class = @envClass</c>).
    /// </summary>
    public const string WorkItemEnvClassEmpty = "orchestration.work_item.env_class.empty";

    /// <summary>WorkItem factory rejected an empty brief (worker brief, stored as jsonb).</summary>
    public const string WorkItemBriefEmpty = "orchestration.work_item.brief.empty";

    /// <summary>WorkItem factory received an initial status that is not Queued or Blocked.</summary>
    public const string WorkItemInitialStatusInvalid = "orchestration.work_item.initial_status.invalid";

    /// <summary>WorkItem attempted an illegal status transition (not in <c>WorkItemTransitions.table</c>).</summary>
    public const string WorkItemIllegalTransition = "orchestration.work_item.illegal_transition";

    /// <summary>Run attempted an illegal status transition (not in <c>RunTransitions.table</c>).</summary>
    public const string RunIllegalTransition = "orchestration.run.illegal_transition";

    /// <summary>
    /// Run got a second work-backlink stamp with a different
    /// <c>(task_id, attempt_ordinal)</c> than the first — the work
    /// bridge promised the engine one Task owns this Run; a
    /// collision is a programming error on the bridge side, not
    /// a recoverable condition. The 422 carries the offending
    /// pair so a human can trace the bridge log.
    /// </summary>
    public const string RunWorkBacklinkMismatch = "orchestration.run.work_backlink.mismatch";

    /// <summary>Merge-batch attempted an illegal status transition (not in <c>MergeBatchTransitions.table</c>).</summary>
    public const string MergeBatchIllegalTransition = "orchestration.merge_batch.illegal_transition";

    /// <summary>Merge-queue entry attempted an illegal status transition (not in <c>MergeQueueTransitions.table</c>).</summary>
    public const string MergeQueueIllegalTransition = "orchestration.merge_queue.illegal_transition";
}
