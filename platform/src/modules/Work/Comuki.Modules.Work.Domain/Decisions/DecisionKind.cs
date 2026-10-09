namespace Comuki.Modules.Work.Domain.Decisions;

/// <summary>
/// Closed set of resolution <see cref="Decision"/> shapes a
/// <see cref="WorkTask"/> accepts. Each
/// kind maps to a deterministic handler in
/// <c>Comuki.Modules.Work.Application.Decisions</c>; an LLM path never
/// holds a token that calls <c>Work.Resolution</c> directly
/// (<see cref="DecisionProvenance"/> rules + the umbrella's task 5.6 invariant).
/// </summary>
public enum DecisionKind
{
    /// <summary>The Task exhausted attempts — create attempt N+1 and dispatch again.</summary>
    Retry = 1,

    /// <summary>The Task exhausted attempts with a replaced brief — mark current <c>Replaced</c>; a fresh Task inherits the inbound id.</summary>
    Replacement = 2,

    /// <summary>The Task exhausted attempts with explicit human waiver — <c>Blocked</c> → <c>Resolved</c> with outcome <c>Waived</c>.</summary>
    Waiver = 3,

    /// <summary>The Task exhausted attempts with no further action possible — <c>Blocked</c> → <c>Resolved</c> with outcome <c>Failed</c>.</summary>
    FailedResolution = 4,

    /// <summary>Cancel the Task from any non-terminal status — <c>Cancelled</c> with the active attempt cleared.</summary>
    Cancellation = 5,
}
