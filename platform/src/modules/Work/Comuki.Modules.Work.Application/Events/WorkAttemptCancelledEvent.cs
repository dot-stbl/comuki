namespace Comuki.Modules.Work.Application.Events;

/// <summary>
/// Wire-format envelope for <c>work.task.attempt-cancelled.v1</c>.
/// Distinct from <see cref="WorkTaskEvent"/>: the Run id is the
/// load-bearing field here (the engine-side cancel port needs it),
/// and the Task's <c>ActiveAttemptId</c> was already cleared before
/// the event is published — the broad
/// <see cref="WorkTaskEvent.ActiveAttemptId"/> field would be
/// <c>null</c> at the publish site and useless downstream. Carrying
/// the cancelled Run id here is the explicit
/// forward-the-cancellation contract; <see cref="WorkTaskEvent"/>
/// stays the lifecycle envelope.
///
/// The envelope reuses <c>TaskId</c> / <c>ProjectId</c> /
/// <c>BriefVersion</c> for the dedupe ledger
/// (<c>work.task.{taskId}:attempt-cancelled:{briefVersion}</c>) —
/// the brief version is the only non-id monotonic counter on the
/// envelope.
/// </summary>
public sealed record WorkAttemptCancelledEvent(
    Guid TaskId,
    Guid ProjectId,
    Guid RunId,
    int BriefVersion,
    DateTimeOffset OccurredAt);
