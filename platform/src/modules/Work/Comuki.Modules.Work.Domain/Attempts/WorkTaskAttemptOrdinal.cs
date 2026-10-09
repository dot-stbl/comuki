namespace Comuki.Modules.Work.Domain.Attempts;

/// <summary>
/// Positive, monotonic per-Task attempt ordinal — a Task owns an
/// ordered sequence of Run attempts; a new attempt is always
/// <c>previous + 1</c> and never resurrects a terminal ordinal
/// (per <c>add-work-management/specs/work-management/spec.md</c>
/// Requirement "Sequential Run attempts"). Storage shape: the bare
/// <c>int</c> on the Task entity, with this value-object as the
/// per-append guard.
/// </summary>
/// <param name="Value">
/// The non-negative ordinal — <c>0</c> means "no attempts yet",
/// <c>1</c> means "first attempt", etc.
/// </param>
public readonly record struct WorkTaskAttemptOrdinal(int Value)
{
    /// <summary>The ordinal used by a fresh Task with no attempts yet.</summary>
    public static WorkTaskAttemptOrdinal None { get; } = new(0);

    /// <summary>The next attempt ordinal a Task appends when one attempt is already active.</summary>
    public static WorkTaskAttemptOrdinal Next(WorkTaskAttemptOrdinal current)
    {
        return new WorkTaskAttemptOrdinal(current.Value + 1);
    }

    /// <summary>True when this ordinal represents "no attempts yet" (the Task has not been dispatched).</summary>
    public bool IsNone => Value == 0;

    /// <summary>True when the Task has at least one attempt recorded.</summary>
    public bool HasAttempt => Value > 0;
}
