namespace Comuki.Modules.Work.Domain.Exceptions;

/// <summary>
/// Stable machine-readable error codes for Work-domain invariant
/// violations. Clients branch on <c>Code</c>, never on the human
/// <c>Message</c> (mirrors the contract on
/// <see cref="Shared.Kernel.Exceptions.DomainException"/>). Codes
/// follow the <c>work.&lt;aggregate&gt;.&lt;rule&gt;</c> dot.case
/// pattern, OTel-tag friendly
/// (<see cref="WorkTaskDomainException"/> surfaces them as
/// ProblemDetails <c>code</c>).
/// </summary>
public static class WorkTaskErrorCodes
{
    /// <summary>The Task factory rejected an empty or whitespace title.</summary>
    public const string TitleEmpty = "work.task.title.empty";

    /// <summary>The Task factory rejected an empty or whitespace brief.</summary>
    public const string BriefEmpty = "work.task.brief.empty";

    /// <summary>The Task factory received an invalid project id (zero guid).</summary>
    public const string ProjectIdEmpty = "work.task.project_id.empty";

    /// <summary>The Task factory received a Task with zero source references — a Task must aggregate at least one.</summary>
    public const string NoSourceRefs = "work.task.sources.empty";

    /// <summary>The Task factory received more than one primary source — a Task has at most one primary.</summary>
    public const string MultiplePrimarySources = "work.task.sources.multiple_primary";

    /// <summary>A lookup by work task id could not find the row — semantic <c>404</c> on a task id.</summary>
    public const string TaskNotFound = "work.task.not_found";

    /// <summary>The worker-side inbox ledger was claimed but the inbound binding row is missing — data inconsistency (a forced-clear race).</summary>
    public const string InboxBindingMissing = "work.task.inbox.binding_missing";

    /// <summary>The dispatch run handler was called with a null work dispatch item.</summary>
    public const string DispatchPayloadMissing = "work.task.dispatch.payload_missing";

    /// <summary>An attempt was appended when an attempt was already active — one-active-Run invariant violated.</summary>
    public const string RunAlreadyActive = "work.task.run.active";

    /// <summary>An attempt was appended while the Task was in a status that does not allow a new attempt (must be Ready or Blocked).</summary>
    public const string AttemptNotAllowed = "work.task.attempt.not_allowed";

    /// <summary>An attempt transition was attempted with a non-monotonic attempt ordinal.</summary>
    public const string AttemptOrdinalNonMonotonic = "work.task.attempt.ordinal.non_monotonic";

    /// <summary>An illegal status transition was attempted (not in <see cref="WorkTaskTransitions"/>).</summary>
    public const string IllegalTransition = "work.task.illegal_transition";

    /// <summary>A <c>Resolved</c> Task was assigned an outcome incompatible with the transition path.</summary>
    public const string ResolutionOutcomeRequired = "work.task.resolution.outcome_required";

    /// <summary>A non-<c>Resolved</c> Task carried a non-empty resolution outcome.</summary>
    public const string ResolutionOutcomeNotAllowed = "work.task.resolution.outcome_not_allowed";

    /// <summary>A <c>Resolved</c> Task was assigned a new outcome after one was already set.</summary>
    public const string ResolutionOutcomeImmutable = "work.task.resolution.outcome_immutable";

    /// <summary>The Task attempted a Mission detachment (removing an attached Mission) — a one-way guard.</summary>
    public const string MissionAttachmentImmutable = "work.task.mission.attachment_immutable";

    /// <summary>The Task attempted to transfer from one Mission to another — a one-way guard.</summary>
    public const string MissionTransferForbidden = "work.task.mission.transfer_forbidden";

    /// <summary>The Task was created without a <see cref="Completion.WorkTaskCompletionPolicy"/> — every Task must carry one.</summary>
    public const string CompletionPolicyMissing = "work.completion.policy_missing";

    /// <summary>A <c>Work.Resolve</c> did not carry every <see cref="Completion.EvidenceKind"/> the policy demands.</summary>
    public const string CompletionEvidenceIncomplete = "work.completion.evidence_incomplete";

    /// <summary>The <c>Work.Resolve</c> actor is the same as the last terminal attempt's authoring actor — reviewer separation violated.</summary>
    public const string CompletionReviewerSeparation = "work.completion.reviewer_separation";

    /// <summary>The Task attempted to change primary source to a related-source ref that does not exist.</summary>
    public const string PrimarySourceNotFound = "work.task.sources.primary_not_found";

    /// <summary>The same actor proposed and approved a human assignment — self-approval is rejected.</summary>
    public const string AssignmentSelfApproval = "work.task.assignment.self_approval";

    /// <summary>The Task already has an assignment row for this (kind, actor id) pair — uniqueness invariant.</summary>
    public const string AssignmentAlreadyExists = "work.task.assignment.duplicate";

    /// <summary>The Task attempted to add a dependency edge pointing at itself — self-loops are rejected.</summary>
    public const string DependencySelfEdge = "work.task.dependency.self_edge";

    /// <summary>The Task attempted to add a dependency edge that already exists for the same prerequisite and kind.</summary>
    public const string DependencyDuplicate = "work.task.dependency.duplicate";
}
