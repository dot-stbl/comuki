using Comuki.Modules.Work.Domain.Exceptions;

namespace Comuki.Modules.Work.Domain.Sources;

/// <summary>
/// One source reference on a <see cref="WorkTask"/> — a tracker-side
/// pointer (or a Native UI pointer) the Task was admitted from. A
/// Task aggregates at least one source ref; exactly one is
/// <see cref="IsPrimary"/> (per task 7.1 of the change, landed
/// later in the umbrella). The previous primary is kept as a related
/// source with a link note; the lifecycle sync targets only the
/// current primary.
/// </summary>
public sealed class WorkTaskSourceRef
{
    private WorkTaskSourceRef(
        WorkTaskSourceKind kind,
        string externalId,
        string displayName,
        bool isPrimary,
        string? linkNote)
        : this(id: Guid.CreateVersion7(), kind: kind, externalId: externalId, displayName: displayName, isPrimary: isPrimary, linkNote: linkNote)
    {
    }

    private WorkTaskSourceRef(
        Guid id,
        WorkTaskSourceKind kind,
        string externalId,
        string displayName,
        bool isPrimary,
        string? linkNote)
    {
        Id = id;
        Kind = kind;
        ExternalId = externalId;
        DisplayName = displayName;
        IsPrimary = isPrimary;
        LinkNote = linkNote;
    }

    /// <summary>
    /// EF-rehydrate factory — carries the persisted <see cref="Id"/> from
    /// the storage row into the aggregate so a re-read sees the same
    /// source-ref identity it had on the prior save. The constructor
    /// is <c>internal</c> so only the Infrastructure project (covered
    /// by <c>InternalsVisibleTo</c>) can call it; the public
    /// <see cref="Primary"/> / <see cref="Related"/> factories are for
    /// authoring a new ref from scratch with a freshly-minted id.
    /// </summary>
    internal static WorkTaskSourceRef Rehydrate(
        Guid id,
        WorkTaskSourceKind kind,
        string externalId,
        string displayName,
        bool isPrimary,
        string? linkNote)
    {
        return new WorkTaskSourceRef(id, kind, externalId, displayName, isPrimary, linkNote);
    }

    /// <summary>Stable identifier — used by <see cref="WorkTask.ChangePrimarySource"/> to address the source ref.</summary>
    public Guid Id { get; }

    /// <summary>The source kind (which tracker / surface admitted the Task).</summary>
    public WorkTaskSourceKind Kind { get; }

    /// <summary>The fully-qualified external identifier within <see cref="Kind"/>.</summary>
    public string ExternalId { get; }

    /// <summary>Opaque display name shown on Task reads; free-form, never parsed.</summary>
    public string DisplayName { get; }

    /// <summary>True when this ref is the Task's current primary; exactly one per Task.</summary>
    public bool IsPrimary { get; }

    /// <summary>Optional link note attached to a former primary that became related (per task 7.2).</summary>
    public string? LinkNote { get; }

    /// <summary>
    /// Creates a primary source reference. The first source added to
    /// a Task is primary by default; subsequent sources are
    /// non-primary until an authorized Decision promotes one (task
    /// 7.2). The <c>externalId</c> must be non-empty; the
    /// <c>displayName</c> is free-form but must be non-empty for
    /// dashboard rendering.
    /// </summary>
    /// <exception cref="WorkTaskDomainException">A required field is empty or whitespace.</exception>
    public static WorkTaskSourceRef Primary(
        WorkTaskSourceKind kind,
        string externalId,
        string displayName,
        string? linkNote = null)
    {
        return Create(kind, externalId, displayName, isPrimary: true, linkNote);
    }

    /// <summary>
    /// Creates a non-primary source reference. The link note is
    /// mandatory when this ref represents a former primary
    /// (downstream promotion via <c>ChangePrimarySourceDecision</c>);
    /// for an unrelated multi-source case, callers may pass null.
    /// </summary>
    /// <exception cref="WorkTaskDomainException">A required field is empty or whitespace.</exception>
    public static WorkTaskSourceRef Related(
        WorkTaskSourceKind kind,
        string externalId,
        string displayName,
        string? linkNote = null)
    {
        return Create(kind, externalId, displayName, isPrimary: false, linkNote);
    }

    private static WorkTaskSourceRef Create(
        WorkTaskSourceKind kind,
        string externalId,
        string displayName,
        bool isPrimary,
        string? linkNote)
    {
        return string.IsNullOrWhiteSpace(externalId)
            ? throw new WorkTaskDomainException(
                WorkTaskErrorCodes.TitleEmpty,
                "source ref external id must not be empty")
            : string.IsNullOrWhiteSpace(displayName)
            ? throw new WorkTaskDomainException(
                WorkTaskErrorCodes.TitleEmpty,
                "source ref display name must not be empty")
            : new WorkTaskSourceRef(kind, externalId, displayName, isPrimary, linkNote);
    }

    /// <summary>Promote this source to primary — used by <see cref="WorkTask.ChangePrimarySource"/>.</summary>
    public WorkTaskSourceRef AsPrimary()
    {
        return new(Kind, ExternalId, DisplayName, isPrimary: true, LinkNote);
    }

    /// <summary>Demote a former primary to a related one with a <paramref name="demotionNote"/>.</summary>
    public WorkTaskSourceRef AsRelated(string demotionNote)
    {
        return new(Kind, ExternalId, DisplayName, isPrimary: false, linkNote: demotionNote);
    }
}
