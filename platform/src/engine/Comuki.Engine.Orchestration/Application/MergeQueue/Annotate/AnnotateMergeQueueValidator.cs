using FluentValidation;

namespace Comuki.Engine.Orchestration.Application.MergeQueue.Annotate;

/// <summary>
/// Structural validation of <see cref="AnnotateMergeQueueCommand"/>:
/// non-empty entry id, bounded notes (null is allowed — clears notes).
/// </summary>
public sealed class AnnotateMergeQueueValidator : AbstractValidator<AnnotateMergeQueueCommand>
{
    /// <summary>Maximum notes length we accept.</summary>
    public const int MaxNotesLength = 4096;

    /// <summary>Rules.</summary>
    public AnnotateMergeQueueValidator()
    {
        RuleFor(static command => command.EntryId)
            .NotEqual(Guid.Empty);

        RuleFor(static command => command.Notes)
            .MaximumLength(MaxNotesLength);
    }
}
