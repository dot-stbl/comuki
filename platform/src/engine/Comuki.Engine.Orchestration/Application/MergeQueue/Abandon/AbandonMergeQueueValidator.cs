using FluentValidation;

namespace Comuki.Engine.Orchestration.Application.MergeQueue.Abandon;

/// <summary>
/// Structural validation of <see cref="AbandonMergeQueueCommand"/>:
/// non-empty entry id, non-empty bounded reason. The domain factory
/// re-enforces both at the store.
/// </summary>
public sealed class AbandonMergeQueueValidator : AbstractValidator<AbandonMergeQueueCommand>
{
    /// <summary>Maximum abandon-reason length we accept.</summary>
    public const int MaxReasonLength = 1024;

    /// <summary>Rules.</summary>
    public AbandonMergeQueueValidator()
    {
        RuleFor(static command => command.EntryId)
            .NotEqual(Guid.Empty);

        RuleFor(static command => command.Reason)
            .NotEmpty()
            .MaximumLength(MaxReasonLength);
    }
}
