namespace Comuki.Modules.Work.Application.Events;

/// <summary>
/// Wire-format envelope for <see cref="WorkTaskEventTypes.Created"/> /
/// <see cref="WorkTaskEventTypes.Readied"/> /
/// <see cref="WorkTaskEventTypes.AttemptRequested"/> /
/// <see cref="WorkTaskEventTypes.Blocked"/> /
/// <see cref="WorkTaskEventTypes.Resolved"/> /
/// <see cref="WorkTaskEventTypes.Cancelled"/> — they all carry
/// this shape. The <c>Created</c> envelope also names the
/// <c>AdmittedEvent</c> in <see cref="Admission.AdmitTaskHandler"/>
/// for first-admission consumers; <see cref="WorkTaskEvent"/> is
/// the broader post-admission envelope every later lifecycle event
/// uses. The payload is the event body the host-side outbox
/// publisher serialises; consumers project it into a domain read
/// model (WorkTaskSummary) per their needs. Field names are
/// camelCase to match the JSON wire form (System.Text.Json default).
/// </summary>
public sealed record WorkTaskEvent(
    Guid TaskId,
    Guid ProjectId,
    string Status,
    int? AttemptOrdinal,
    Guid? ActiveAttemptId,
    int? BriefVersion,
    string? ResolutionOutcome,
    string? MissionId,
    DateTimeOffset OccurredAt);
