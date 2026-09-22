using FluentValidation;

namespace Comuki.Engine.Orchestration.Application.MergeQueue.MergeEntry;

/// <summary>
/// Structural validation of <see cref="MergeMergeQueueCommand"/>:
/// non-empty entry id. The domain factory re-enforces the
/// in-progress precondition.
/// </summary>
public sealed class MergeMergeQueueValidator : AbstractValidator<MergeMergeQueueCommand>
{
    /// <summary>Rules.</summary>
    public MergeMergeQueueValidator()
    {
        RuleFor(static command => command.EntryId)
            .NotEqual(Guid.Empty);
    }
}
