using Comuki.Modules.Work.Domain.Assignments;
using Comuki.Modules.Work.Domain.Attempts;
using Comuki.Modules.Work.Domain.Completion;
using Comuki.Modules.Work.Domain.Dependencies;
using Comuki.Modules.Work.Domain.Exceptions;
using Comuki.Modules.Work.Domain.Ids;
using Comuki.Modules.Work.Domain.Sources;
using Comuki.Modules.Work.Domain.Visibility;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Work.Domain;

/// <summary>
/// WorkTask — the durable user-facing unit of work. A Task owns a
/// versioned brief, one or more source references (exactly one
/// primary), a dependency graph, a monotonic attempt ledger, and
/// exactly one active Run attempt at a time. Status transitions
/// follow the table in <see cref="WorkTaskTransitions"/>; the
/// resolution outcome is set exactly once on the
/// <c>→ Resolved</c> edge and is immutable thereafter. Visibility
/// transitions Standalone → Mission once and only once; the
/// aggregate has no detachment or Mission-transfer edge (per
/// <c>add-work-management/specs/work-management/spec.md</c>
/// Requirement "Task visibility follows Mission attachment").
/// </summary>
public sealed class WorkTask
{
    private readonly List<WorkTaskSourceRef> sourceRefs = [];
    private readonly List<WorkTaskDependency> dependencies = [];
    private readonly List<WorkTaskAssignment> assignments = [];

    private WorkTask(
        WorkTaskId id,
        ProjectId projectId,
        string title,
        string brief,
        int briefVersion,
        WorkTaskAttemptOrdinal attemptOrdinal,
        RunId? activeAttemptId,
        TaskVisibility visibility,
        MissionId? missionId,
        WorkTaskStatus status,
        WorkTaskResolutionOutcome? resolutionOutcome,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        ProjectId = projectId;
        Title = title;
        Brief = brief;
        BriefVersion = briefVersion;
        AttemptOrdinal = attemptOrdinal;
        ActiveAttemptId = activeAttemptId;
        Visibility = visibility;
        MissionId = missionId;
        Status = status;
        ResolutionOutcome = resolutionOutcome;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// EF-materialisation factory — recreates an aggregate from its
    /// persisted state, including the side collections
    /// (<see cref="SourceRefs"/> / <see cref="Dependencies"/>). The
    /// constructor is internal so only the Infrastructure project
    /// (covered by <c>InternalsVisibleTo</c>) can call it; the public
    /// factory <see cref="Create"/> is for new aggregates that go
    /// through the domain invariant guards. A rehydrated Task skips
    /// every guard — the persisted state is, by definition, valid;
    /// re-running the guards would also reject already-resolved
    /// Tasks whose resolution outcome was set under a previous
    /// version of the transition table.
    /// </summary>
    internal WorkTask(
        WorkTaskId id,
        ProjectId projectId,
        string title,
        string brief,
        int briefVersion,
        WorkTaskAttemptOrdinal attemptOrdinal,
        RunId? activeAttemptId,
        TaskVisibility visibility,
        MissionId? missionId,
        WorkTaskStatus status,
        WorkTaskResolutionOutcome? resolutionOutcome,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        IEnumerable<WorkTaskSourceRef> sourceRefs,
        IEnumerable<WorkTaskDependency> dependencies)
    {
        Id = id;
        ProjectId = projectId;
        Title = title;
        Brief = brief;
        BriefVersion = briefVersion;
        AttemptOrdinal = attemptOrdinal;
        ActiveAttemptId = activeAttemptId;
        Visibility = visibility;
        MissionId = missionId;
        Status = status;
        ResolutionOutcome = resolutionOutcome;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        this.sourceRefs.AddRange(sourceRefs);
        this.dependencies.AddRange(dependencies);
    }

    // Brief is set in the constructor body above; the property's private set is used
    // by the revision path in ReviseBrief. The Title and Id are read-only and never
    // mutate after construction.

    /// <summary>Strong-typed Task id (UUIDv7).</summary>
    public WorkTaskId Id { get; }

    /// <summary>Project the Task belongs to.</summary>
    public ProjectId ProjectId { get; }

    /// <summary>Human-readable Task title — set at create and read-only after that.</summary>
    public string Title { get; }

    /// <summary>Current brief (immutable per version; revisions bump <see cref="BriefVersion"/>).</summary>
    public string Brief { get; private set; }

    /// <summary>Brief version counter — starts at 1, increments on every <see cref="ReviseBrief"/> call.</summary>
    public int BriefVersion { get; private set; }

    /// <summary>Current lifecycle status; mutated only via <see cref="TransitionTo"/>.</summary>
    public WorkTaskStatus Status { get; private set; }

    /// <summary>Resolution outcome — null while non-terminal; set exactly once on <c>→ Resolved</c>; immutable thereafter.</summary>
    public WorkTaskResolutionOutcome? ResolutionOutcome { get; private set; }

    /// <summary>Monotonic per-Task attempt ordinal — <see cref="WorkTaskAttemptOrdinal.None"/> while no attempt has been appended.</summary>
    public WorkTaskAttemptOrdinal AttemptOrdinal { get; private set; }

    /// <summary>The Run id of the active attempt; null while no attempt is in flight.</summary>
    public RunId? ActiveAttemptId { get; private set; }

    /// <summary>True when an attempt is in flight (mirrors <c>ActiveAttemptId is { }</c>; read-only convenience for guards).</summary>
    public bool HasActiveAttempt => ActiveAttemptId is not null;

    /// <summary>Task visibility scope — see <see cref="TaskVisibility"/>.</summary>
    public TaskVisibility Visibility { get; private set; }

    /// <summary>Mission id when <see cref="Visibility"/> is <see cref="TaskVisibility.Mission"/>; null while standalone.</summary>
    public MissionId? MissionId { get; private set; }

    /// <summary>The Task's source references — at least one, exactly one primary. Read-only view; mutate via the aggregate methods.</summary>
    public IReadOnlyList<WorkTaskSourceRef> SourceRefs => sourceRefs;

    /// <summary>The Task's outgoing dependency edges. Read-only view; mutate via the aggregate methods.</summary>
    public IReadOnlyList<WorkTaskDependency> Dependencies => dependencies;

    /// <summary>Responsible-actor assignments (audit + read projection; NOT authorization — task 7.3 invariant).</summary>
    public IReadOnlyList<WorkTaskAssignment> Assignments => assignments;

    /// <summary>The versioned completion policy attached to this Task; never null after creation.</summary>
    public WorkTaskCompletionPolicy CompletionPolicy
    {
        get => field
        ?? throw new WorkTaskDomainException(
            WorkTaskErrorCodes.CompletionPolicyMissing,
            "the task was constructed without a completion policy"); private set;
    }

    /// <summary>The last terminal attempt's authoring record — used for reviewer-separation (task 8.3).</summary>
    public ReviewerSeparationAttempt? LastTerminalAttempt { get; private set; }

    /// <summary>When the Task was first created.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Last status / version / source / dependency / visibility change timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a Task in <see cref="WorkTaskStatus.Draft"/> with no
    /// attempts and the supplied source refs. The first source
    /// ref added via <see cref="AddSourceRef"/> is primary by
    /// default; the factory itself takes the first primary to
    /// avoid a bootstrap step that has to know the aggregate's
    /// internal ordering. <paramref name="title"/> and
    /// <paramref name="brief"/> are non-empty / non-whitespace;
    /// <paramref name="primarySource"/> is the primary that
    /// bootstraps the source list. A
    /// <paramref name="completionPolicy"/> is required — every
    /// Task carries one from creation (task 8.1 invariant).
    /// </summary>
    /// <exception cref="WorkTaskDomainException">A factory invariant was violated.</exception>
    public static WorkTask Create(
        ProjectId projectId,
        string title,
        string brief,
        WorkTaskSourceRef primarySource,
        WorkTaskCompletionPolicy completionPolicy,
        DateTimeOffset now)
    {
        if (projectId.Value == Guid.Empty)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.ProjectIdEmpty,
                "project id must be a non-empty guid");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.TitleEmpty,
                "title must not be empty or whitespace");
        }

        if (string.IsNullOrWhiteSpace(brief))
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.BriefEmpty,
                "brief must not be empty or whitespace");
        }

        if (primarySource is null)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.NoSourceRefs,
                "a task must aggregate at least one source ref");
        }

        if (!primarySource.IsPrimary)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.MultiplePrimarySources,
                "the bootstrap source ref must be primary");
        }

        if (completionPolicy is null || !completionPolicy.Contract.IsWellFormed)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.CompletionPolicyMissing,
                "a task must carry a non-empty completion policy at creation");
        }

        var id = WorkTaskId.New();
        var task = new WorkTask(
            id,
            projectId,
            title.Trim(),
            brief.Trim(),
            briefVersion: 1,
            attemptOrdinal: WorkTaskAttemptOrdinal.None,
            activeAttemptId: null,
            visibility: TaskVisibility.Project,
            missionId: null,
            status: WorkTaskStatus.Draft,
            resolutionOutcome: null,
            createdAt: now,
            updatedAt: now);
        task.sourceRefs.Add(primarySource);
        task.CompletionPolicy = completionPolicy;
        return task;
    }

    /// <summary>
    /// Adds a source ref. The first source is set via
    /// <see cref="Create"/>; this method appends a
    /// non-primary <see cref="WorkTaskSourceRef"/> to the
    /// aggregate (a former primary may become a related source via
    /// the authorized Decision handler — task 7.2 — and that path
    /// is layered on top of this one, not a separate factory).
    /// </summary>
    /// <exception cref="WorkTaskDomainException">A duplicate primary was supplied, or the source kind / id is already present.</exception>
    public void AddSourceRef(WorkTaskSourceRef source, DateTimeOffset now)
    {
        if (source is null)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.NoSourceRefs,
                "source ref must not be null");
        }

        if (source.IsPrimary)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.MultiplePrimarySources,
                "primary is fixed at create; promoted sources land via the Decision handler (task 7.2)");
        }

        if (sourceRefs.Any(existing =>
                existing.Kind == source.Kind
                && string.Equals(existing.ExternalId, source.ExternalId, StringComparison.Ordinal)))
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.NoSourceRefs,
                "a source ref for this kind / external id is already aggregated");
        }

        sourceRefs.Add(source);
        UpdatedAt = now;
    }

    /// <summary>
    /// Revises the brief; bumps <see cref="BriefVersion"/>. Allowed
    /// in any non-terminal status (operators revise a Draft
    /// before dispatch; revisions during Blocked are common too).
    /// </summary>
    /// <exception cref="WorkTaskDomainException">The Task is terminal or the new brief is empty.</exception>
    public void ReviseBrief(string newBrief, DateTimeOffset now)
    {
        if (Status == WorkTaskStatus.Resolved || Status == WorkTaskStatus.Cancelled)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.IllegalTransition,
                $"cannot revise brief on a {Status} task");
        }

        if (string.IsNullOrWhiteSpace(newBrief))
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.BriefEmpty,
                "new brief must not be empty or whitespace");
        }

        BriefVersion++;
        // Title is immutable; Brief is the only mutable string on the aggregate, mutated here only.
        Brief = newBrief.Trim();
        UpdatedAt = now;
    }

    /// <summary>
    /// Applies a status transition. The transition must be in
    /// <see cref="WorkTaskTransitions"/>. When
    /// <paramref name="to"/> is
    /// <see cref="WorkTaskStatus.Resolved"/>, an
    /// <paramref name="outcome"/> is required and
    /// stored immutably; for every other transition, no
    /// outcome is allowed.
    /// </summary>
    /// <exception cref="WorkTaskDomainException">The transition is not in <see cref="WorkTaskTransitions"/>, the outcome is missing, or the outcome was already set.</exception>
    public void TransitionTo(WorkTaskStatus to, WorkTaskResolutionOutcome? outcome, DateTimeOffset now)
    {
        if (!WorkTaskTransitions.IsLegal(Status, to))
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.IllegalTransition,
                $"illegal work task transition {Status} -> {to}");
        }

        if (to == WorkTaskStatus.Resolved)
        {
            if (outcome is null || !outcome.Value.HasValue)
            {
                throw new WorkTaskDomainException(
                    WorkTaskErrorCodes.ResolutionOutcomeRequired,
                    "transition to Resolved requires a non-Unspecified outcome");
            }

            if (ResolutionOutcome is { HasValue: true })
            {
                throw new WorkTaskDomainException(
                    WorkTaskErrorCodes.ResolutionOutcomeImmutable,
                    "resolution outcome is already set and is immutable");
            }

            ResolutionOutcome = outcome;
        }
        else if (outcome is { HasValue: true })
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.ResolutionOutcomeNotAllowed,
                $"outcome may only be set on transition to {WorkTaskStatus.Resolved}, not on {to}");
        }

        // Cancellation clears any active attempt in the same
        // transition — task 5.5 invariant. The Task is irreversible;
        // the attempt ledger stays intact for the audit / Run-side
        // record, but the WorkTask no longer points to an active Run.
        if (to == WorkTaskStatus.Cancelled && ActiveAttemptId is not null)
        {
            ActiveAttemptId = null;
        }

        Status = to;
        UpdatedAt = now;
    }

    /// <summary>
    /// Appends a new Run attempt. The Task must be in
    /// <see cref="WorkTaskStatus.Ready"/> or
    /// <see cref="WorkTaskStatus.Blocked"/>, must NOT have an
    /// active attempt, and the run id must not match the active
    /// attempt (a duplicate launch with the same run id is a
    /// no-op caller-side, not a fresh attempt). Returns the
    /// new attempt ordinal.
    /// </summary>
    /// <returns>The new monotonic attempt ordinal (previous + 1).</returns>
    /// <exception cref="WorkTaskDomainException">A guard was violated (active attempt already, or status not Ready / Blocked).</exception>
    public WorkTaskAttemptOrdinal AppendAttempt(RunId runId, DateTimeOffset now)
    {
        if (Status != WorkTaskStatus.Ready && Status != WorkTaskStatus.Blocked)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.AttemptNotAllowed,
                $"a new attempt may only be appended in {WorkTaskStatus.Ready} or {WorkTaskStatus.Blocked}, got {Status}");
        }

        if (ActiveAttemptId is { } existing && existing == runId)
        {
            // Same-run re-launch (caller-side retry of the inbox
            // claim): the active attempt is the same; do not
            // bump ordinal. This is the idempotency seam — it lets
            // a losing / retried dispatch stay on the same
            // attempt instead of creating attempt N + 1.
            return AttemptOrdinal;
        }

        if (ActiveAttemptId is not null)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.RunAlreadyActive,
                $"task {Id} already has an active attempt (run {ActiveAttemptId}); concurrent dispatch is not allowed");
        }

        AttemptOrdinal = WorkTaskAttemptOrdinal.Next(AttemptOrdinal);
        ActiveAttemptId = runId;
        UpdatedAt = now;
        return AttemptOrdinal;
    }

    /// <summary>
    /// Marks the active attempt as completed (terminal-success or
    /// terminal-failed); clears <see cref="ActiveAttemptId"/> so a
    /// subsequent <see cref="AppendAttempt"/> starts a fresh
    /// attempt ordinal. The Task stays in its current status — the
    /// caller (inbox consumer) drives the status transition via
    /// <see cref="TransitionTo"/> based on the terminal outcome.
    /// </summary>
    /// <exception cref="WorkTaskDomainException">No active attempt or run id mismatch.</exception>
    public void CompleteAttempt(RunId runId, DateTimeOffset now)
    {
        if (ActiveAttemptId is null)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.AttemptNotAllowed,
                "no active attempt to complete");
        }

        if (ActiveAttemptId != runId)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.AttemptNotAllowed,
                $"active attempt is run {ActiveAttemptId}, not {runId}");
        }

        ActiveAttemptId = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Attaches the Task to a Mission — the one-way visibility
    /// transition. The Task must currently be standalone
    /// (<see cref="Visibility"/> = <see cref="TaskVisibility.Project"/>);
    /// after attachment, the Task and prior / future Runs and
    /// artifacts require Mission access (per spec "Task visibility
    /// follows Mission attachment"). There is no
    /// <c>DetachFromMission</c> and no
    /// <c>TransferToMission(missionId)</c> on purpose.
    /// </summary>
    /// <exception cref="WorkTaskDomainException">The Task is already Mission-attached.</exception>
    public void AttachToMission(MissionId missionId, DateTimeOffset now)
    {
        if (Visibility == TaskVisibility.Mission)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.MissionAttachmentImmutable,
                "task is already attached to a mission; the attachment is one-way");
        }

        Visibility = TaskVisibility.Mission;
        MissionId = missionId;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records an outgoing dependency edge. Self-edges are rejected
    /// by <see cref="WorkTaskDependency.Create"/>; the Task's
    /// <c>Dependent</c> side is always the current id, so the
    /// caller only supplies the prerequisite id and the kind.
    /// </summary>
    /// <exception cref="WorkTaskDomainException">A duplicate edge already exists for the same prerequisite and kind.</exception>
    public void AddDependency(WorkTaskId prerequisite, WorkTaskDependencyKind kind, DateTimeOffset now)
    {
        if (prerequisite == Id)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.DependencySelfEdge,
                "a task cannot depend on itself");
        }

        if (dependencies.Any(edge => edge.Prerequisite == prerequisite && edge.Kind == kind))
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.DependencyDuplicate,
                "duplicate dependency edge for the same prerequisite and kind");
        }

        dependencies.Add(WorkTaskDependency.Create(Id, prerequisite, kind));
        UpdatedAt = now;
    }

    /// <summary>
    /// Promotes an existing related source to primary; the previous
    /// primary becomes a related source with a <c>linkNote</c>
    /// describing the demotion (the umbrella's task 7.2 path — the
    /// new primary receives lifecycle sync events; the demoted
    /// primary and other related sources receive key-Decision
    /// summaries only).
    /// </summary>
    /// <exception cref="WorkTaskDomainException">No source ref with that id exists.</exception>
    public void ChangePrimarySource(Guid newPrimarySourceId, DateTimeOffset now)
    {
        var target = sourceRefs.FirstOrDefault(source => source.Id == newPrimarySourceId)
            ?? throw new WorkTaskDomainException(
                WorkTaskErrorCodes.PrimarySourceNotFound,
                $"no source ref with id {newPrimarySourceId} on task {Id}");

        if (target.IsPrimary)
        {
            return;
        }

        for (var index = 0; index < sourceRefs.Count; index++)
        {
            var current = sourceRefs[index];
            if (current.Id == newPrimarySourceId)
            {
                sourceRefs[index] = current.AsPrimary();
            }
            else if (current.IsPrimary)
            {
                sourceRefs[index] = current.AsRelated("demoted: replaced by ChangePrimarySourceDecision");
            }
        }

        UpdatedAt = now;
    }

    /// <summary>
    /// Adds a responsible-actor assignment. The aggregate guard
    /// rejects duplicate (kind, actor id) rows and self-approval:
    /// the assignment's <see cref="WorkTaskAssignment.ProposedBy"/>
    /// actor must not be the same as the approver the proposal
    /// later transitions to.
    /// </summary>
    /// <exception cref="WorkTaskDomainException">Duplicate (kind, actor id) or self-approval attempt.</exception>
    public void AddAssignment(WorkTaskAssignment assignment)
    {
        if (assignments.Any(existing =>
                existing.ActorKind == assignment.ActorKind
                && string.Equals(existing.ActorId, assignment.ActorId, StringComparison.Ordinal)))
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.AssignmentAlreadyExists,
                $"a {(assignment.ActorKind == AssignmentActorKind.Human ? "human" : "service")} assignment for actor '{assignment.ActorId}' already exists on task {Id}");
        }

        assignments.Add(assignment);
        UpdatedAt = assignment.AssignedAt;
    }

    /// <summary>
    /// Marks a Human-proposed assignment as approved by a distinct
    /// human — the umbrella's task 7.5 invariant (no self-approval).
    /// </summary>
    /// <exception cref="WorkTaskDomainException">No matching assignment, not in Proposed state, or self-approval.</exception>
    public void ApproveAssignment(Guid assignmentId, string approverActorId, DateTimeOffset now)
    {
        var index = FindAssignmentIndex(assignmentId);
        var assignment = assignments[index];

        if (assignment.ProposalState != AssignmentProposalState.Proposed)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.AssignmentAlreadyExists,
                $"assignment {assignmentId} is not in a Proposed state (current: {assignment.ProposalState})");
        }

        if (string.Equals(assignment.ProposedBy, approverActorId, StringComparison.Ordinal))
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.AssignmentSelfApproval,
                $"actor '{approverActorId}' proposed the assignment and cannot approve it");
        }

        assignments[index] = assignment with
        {
            ProposalState = AssignmentProposalState.Approved,
            AssignedAt = now,
        };
        UpdatedAt = now;
    }

    /// <summary>
    /// Rejects a Human-proposed assignment by a distinct human;
    /// the proposal stays in the audit log but the assignment is
    /// not effective.
    /// </summary>
    public void RejectAssignment(Guid assignmentId, string rejecterActorId, DateTimeOffset now)
    {
        var index = FindAssignmentIndex(assignmentId);
        var assignment = assignments[index];

        if (string.Equals(assignment.ProposedBy, rejecterActorId, StringComparison.Ordinal))
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.AssignmentSelfApproval,
                $"actor '{rejecterActorId}' proposed the assignment and cannot reject it");
        }

        assignments[index] = assignment with
        {
            ProposalState = AssignmentProposalState.Rejected,
            AssignedAt = now,
        };
        UpdatedAt = now;
    }

    /// <summary>
    /// Records the latest terminal attempt's authoring actor — the
    /// resolver reads this on <c>Work.Resolve</c> to enforce reviewer
    /// separation (task 8.3). The WorkTaskAttempt row is the source
    /// of truth on the persistence side; this property mirrors the
    /// last terminal record for the aggregate guard.
    /// </summary>
    public void StampLastTerminalAttempt(string authoringActorId, DateTimeOffset terminalAt)
    {
        LastTerminalAttempt = new ReviewerSeparationAttempt(authoringActorId, terminalAt);
    }

    /// <summary>
    /// Aggregate-side reviewer-separation guard — invoked by the
    /// <c>Work.Resolve</c> handler before any state mutation. A
    /// same-actor Resolve is rejected (surfaces as
    /// <c>409 work.completion.reviewer-separation</c>).
    /// </summary>
    /// <exception cref="WorkTaskDomainException">No terminal attempt on record or the same actor authored the last terminal attempt.</exception>
    public void EnsureReviewerSeparation(string resolvingActorId)
    {
        if (LastTerminalAttempt is null)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.CompletionReviewerSeparation,
                "no terminal attempt on record — reviewer separation is not applicable yet");
        }

        if (string.Equals(LastTerminalAttempt.AuthoringActorId, resolvingActorId, StringComparison.Ordinal))
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.CompletionReviewerSeparation,
                $"actor '{resolvingActorId}' authored the last terminal attempt and cannot resolve it");
        }
    }

    /// <summary>
    /// Records the start of a replacement Decision — the new Task
    /// inherits the inbound id; the legacy Task is preserved as a
    /// historical reference. The aggregate side just stamps the
    /// replacement brief + the new outcome; the inbound-id
    /// remapping lives in the consumer (the inbound_item_bindings
    /// table is the source of truth for the inbound → Task mapping).
    /// </summary>
    /// <exception cref="WorkTaskDomainException">The Task is already terminal.</exception>
    public void StampReplacementOutcome(DateTimeOffset now)
    {
        if (Status == WorkTaskStatus.Resolved || Status == WorkTaskStatus.Cancelled)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.IllegalTransition,
                $"cannot stamp a Replacement outcome on a {Status} task");
        }

        TransitionTo(WorkTaskStatus.Resolved, WorkTaskResolutionOutcome.Replaced, now);
    }

    private int FindAssignmentIndex(Guid assignmentId)
    {
        for (var index = 0; index < assignments.Count; index++)
        {
            if (assignments[index].Id == assignmentId)
            {
                return index;
            }
        }

        throw new WorkTaskDomainException(
            WorkTaskErrorCodes.AssignmentAlreadyExists,
            $"no assignment with id {assignmentId} on task {Id}");
    }
}

/// <summary>
/// Strong-typed Mission id. The Mission bounded context lands in
/// <c>add-minimal-missions</c>; in the meantime the type lives here
/// as a forward declaration so the Work domain's visibility guard
/// compiles against a stable shape. Wire form is the underlying
/// <see cref="Guid"/> string.
/// </summary>
public readonly record struct MissionId(Guid Value)
{
    /// <summary>Generates a new UUIDv7 identifier.</summary>
    /// <returns></returns>
    public static MissionId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
