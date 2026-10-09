namespace Comuki.Shared.Contracts.Work;

/// <summary>
/// Work-dispatch payload — the work-management change's wire-format
/// envelope for "the engine should run this attempt of this Task"
/// (decision 2 of <c>add-work-management/design.md</c>: Work ↔
/// Orchestration via durable outbox/inbox, never dual writes). The
/// Work outbox row carries the envelope id
/// <c>work.task.{taskId}:dispatch:{attemptOrdinal}</c> as the
/// idempotency key on the receiving side; a losing / retried
/// <c>DispatchRun</c> resolves to the same Run id (the engine's
/// inbox claim mirrors the admission claim — the work-queue
/// admission idempotency path per task #87). The DTO lives in
/// <c>Shared.Contracts</c> on purpose: the producer (Work) and the
/// consumer (Engine) both bind to this contract, neither imports
/// the other's domain assembly, and the host composes the
/// dispatch path. Wire form is the on-disk JSON
/// (<c>jsonb</c> column on the work outbox row).
/// </summary>
/// <param name="TaskId">Wire-form <c>WorkTaskId</c> as a string; both sides bind to the same canonical form (mirrors the engine's <c>WorkItemId</c> wire shape).</param>
/// <param name="ProjectId">Wire-form <c>ProjectId</c> as a string; the engine receives the dispatch scoped to a project.</param>
/// <param name="AttemptOrdinal">1-based ordinal of the attempt being dispatched. Monotonic per Task; the engine inbox uses it as part of the dedupe key (<c>work.task.{taskId}:dispatch:{attemptOrdinal}</c>).</param>
/// <param name="ProfileKey">The worker profile to launch (e.g. <c>implement</c>, <c>pr-review</c>).</param>
/// <param name="Image">The container image ref the engine should launch for this attempt (profile + commit-pin resolved on the Work side).</param>
/// <param name="EnvClass">The environment class the engine routes this Run to (<c>dev</c>, <c>staging</c>, <c>prod</c>); drives compute policy on the engine side.</param>
/// <param name="ProfilesRef">Pointer to the per-profile override bundle (capabilities, secrets refs) the engine should layer on top of the base image.</param>
/// <param name="Brief">The worker brief — raw JSON (matches the <c>jsonb</c> column shape).</param>
/// <param name="InboundItemExternalId">The Integrations-side external id this Task originated from, if any (the bridge for admission/claim dedupe on the Integrations side); <c>null</c> for Tasks that did not start from an InboundItem.</param>
public sealed record WorkDispatchItem(
    string TaskId,
    string ProjectId,
    int AttemptOrdinal,
    string ProfileKey,
    string Image,
    string EnvClass,
    string ProfilesRef,
    string Brief,
    string? InboundItemExternalId)
{
    /// <summary>
    /// Builds the idempotency key the engine inbox claims against
    /// for a dispatch: <c>work.task.{taskId}:dispatch:{attemptOrdinal}</c>.
    /// The leading <c>work.task.</c> prefix scopes the dedupe
    /// ledger to the Work context — a work-queue claim never
    /// collides with an Integrations-admission claim that uses
    /// the same string shape under a different prefix.
    /// </summary>
    public static string DispatchMessageId(string taskId, int attemptOrdinal)
    {
        return $"work.task.{taskId}:dispatch:{attemptOrdinal}";
    }

    /// <summary>
    /// Builds the idempotency key the engine inbox claims against
    /// for a terminal ingest: <c>work.task.{taskId}:terminal:{attemptOrdinal}</c>.
    /// Distinct from <see cref="DispatchMessageId"/> so a terminal
    /// delivery and a subsequent dispatch for the next attempt do
    /// not collide on the inbox PK — the table partitions by
    /// prefix, not by ordinal.
    /// </summary>
    public static string TerminalMessageId(string taskId, int attemptOrdinal)
    {
        return $"work.task.{taskId}:terminal:{attemptOrdinal}";
    }
}
