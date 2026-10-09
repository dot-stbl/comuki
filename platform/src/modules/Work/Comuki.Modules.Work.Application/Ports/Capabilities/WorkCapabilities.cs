namespace Comuki.Modules.Work.Application.Ports.Capabilities;

/// <summary>
/// Capability descriptors the Work module exposes to downstream
/// consumers (the umbrella's Capability Broker and any future cross-module
/// handshakes). The descriptors are typed records — no string-keyed
/// dictionary, no reflection-based introspection — so the Work module's
/// public surface is a compile-time contract: adding a new capability
/// lights up every consumer's overload resolution without runtime lookup.
/// <para>
/// This is the contract-side half of the umbrella's
/// <c>add-work-management</c> task 3.7 — the full Broker exposure lands
/// in #90's work. For now the descriptors are produced into the Work
/// host's API surface via the
/// <c>Comuki.Host.Work.WorkCapabilitiesView</c> record and
/// surfaced on the <c>/api/v1/work/capabilities</c> endpoint (read-only,
/// permission work:read).
/// </para>
/// <para>
/// Naming follows the umbrella's <c>&lt;context&gt;.&lt;aggregate&gt;.&lt;verb&gt;</c>
/// rule (see <c>openspec/changes/add-mission-cowork/design.md</c> §"Capability
/// naming"). Each descriptor pairs the stable contract name with a
/// short description so a downstream consumer that lists capabilities can
/// render the descriptions verbatim.
/// </para>
/// </summary>
public static class WorkCapabilities
{
    /// <summary>
    /// WorkTask creation — the <c>Work.AdmitTask</c> command surfaces a
    /// new Task from an inbound id. The capability carrier is the
    /// Integrations-side webhook or claim that initiated the inbound;
    /// the Work-side Decision is the <c>AdmitTask</c> command.
    /// </summary>
    public static readonly WorkCapabilityDescriptor TaskCreate =
        new("work.task.create", "Create a WorkTask from an admitted inbound item.");

    /// <summary>
    /// WorkTask revision — change the title, brief, source, completion
    /// policy, or responsible actors. The command lives in
    /// <c>Work.ReviseTask</c> (added in task 7.2 of the umbrella).
    /// </summary>
    public static readonly WorkCapabilityDescriptor TaskRevise =
        new("work.task.revise", "Revise a WorkTask's mutable fields.");

    /// <summary>
    /// WorkTask actor assignment — change the responsible actors. The
    /// command lives in <c>Work.AssignTask</c> (added in task 7.3).
    /// </summary>
    public static readonly WorkCapabilityDescriptor TaskAssign =
        new("work.task.assign", "Assign responsible actors to a WorkTask.");

    /// <summary>
    /// Source-link mutation — add or remove <c>WorkTaskSourceRef</c> rows on
    /// a Task. The command lives in <c>Work.LinkSource</c> (task 7.1).
    /// </summary>
    public static readonly WorkCapabilityDescriptor TaskLinkSource =
        new("work.task.link-source", "Add or remove WorkTaskSourceRef rows.");

    /// <summary>
    /// Dependency mutation — add or remove a <c>WorkTaskDependency</c> row
    /// (blocks / relates-to). The command lives in <c>Work.Relate</c>
    /// (task 7.6). Cross-Mission edges require dual-access; see
    /// <c>add-minimal-missions</c> #93 for the Mission object-policy seam.
    /// </summary>
    public static readonly WorkCapabilityDescriptor TaskRelate =
        new("work.task.relate", "Add or remove a WorkTaskDependency.");

    /// <summary>
    /// Dispatch a WorkTask — the <c>Work.DispatchRun</c> command
    /// (task 2.3). Reuses the engine's inbox-claim idempotency
    /// contract; emits the <c>work.task.dispatch-requested.v1</c>
    /// event in the same transaction as the <c>→ Active</c>
    /// transition.
    /// </summary>
    public static readonly WorkCapabilityDescriptor TaskDispatch =
        new("work.task.dispatch", "Dispatch a Ready or Blocked WorkTask as a new Run attempt.");

    /// <summary>
    /// Cancel an in-flight attempt — the <c>Work.CancelAttempt</c>
    /// command (task 2.4). Reuses the engine's generation-fencing
    /// contract.
    /// </summary>
    public static readonly WorkCapabilityDescriptor TaskCancelAttempt =
        new("work.task.cancel-attempt", "Cancel the active attempt of a non-terminal WorkTask.");

    /// <summary>
    /// Resolve a Blocked WorkTask — the <c>Work.Resolve</c> command
    /// (task 8.2). Carries a Decision (auto, Brain, human) and an
    /// <c>EvidenceContract</c>; reviewer separation is enforced at
    /// the aggregate guard.
    /// </summary>
    public static readonly WorkCapabilityDescriptor TaskResolve =
        new("work.task.resolve", "Resolve a Blocked WorkTask with an authorized Decision.");

    /// <summary>WorkTask collection query (list) — backed by <c>IWorkTaskStore.ListAsync</c>.</summary>
    public static readonly WorkCapabilityDescriptor TasksQuery =
        new("work.tasks.query", "Query the WorkTask collection with filters, sort, and paging.");

    /// <summary>Read one WorkTask by id — the <c>GET /api/v1/work/tasks/{taskId}</c> surface.</summary>
    public static readonly WorkCapabilityDescriptor TasksRead =
        new("work.tasks.read", "Read one WorkTask by id.");

    /// <summary>Read the attempt history of a WorkTask.</summary>
    public static readonly WorkCapabilityDescriptor TasksAttempts =
        new("work.tasks.attempts", "Read the attempt history of a WorkTask.");

    /// <summary>Read the dependency graph of a WorkTask (with redacted stubs for inaccessible cross-Mission edges).</summary>
    public static readonly WorkCapabilityDescriptor TasksDependencies =
        new("work.tasks.dependencies", "Read the dependency graph of a WorkTask.");

    /// <summary>The full set of capability descriptors the Work module exposes — immutable, ordered by contract name.</summary>
    public static readonly IReadOnlyList<WorkCapabilityDescriptor> All =
        [
            TaskAssign,
            TaskCancelAttempt,
            TaskCreate,
            TaskDispatch,
            TaskLinkSource,
            TaskRelate,
            TaskResolve,
            TaskRevise,
            TasksAttempts,
            TasksDependencies,
            TasksQuery,
            TasksRead,
        ];

    /// <summary>
    /// Stable contract identifier for one capability the Work module
    /// advertises. The descriptor is immutable; consumers compare
    /// instances by <see cref="Name"/> (case-sensitive, ordinal).
    /// </summary>
    /// <param name="Name">Stable dot.case identifier — see <c>add-mission-cowork/design.md</c> §"Capability naming".</param>
    /// <param name="Description">Human-readable description of the capability; surfaced by the <c>/api/v1/work/capabilities</c> endpoint and indexed by the FE permission helper.</param>
    public sealed record WorkCapabilityDescriptor(string Name, string Description);
}
