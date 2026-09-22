using FluentValidation;

namespace Comuki.Engine.Orchestration.Application.MergeQueue.Release;

/// <summary>
/// Structural validation of <see cref="ReleaseMergeQueueCommand"/>:
/// non-empty entry id. The domain factory re-enforces the
/// in-progress precondition.
/// </summary>
public sealed class ReleaseMergeQueueValidator : AbstractValidator<ReleaseMergeQueueCommand>
{
    /// <summary>Rules.</summary>
    public ReleaseMergeQueueValidator()
    {
        RuleFor(static command => command.EntryId)
            .NotEqual(Guid.Empty);
    }
}
