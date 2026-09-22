using FluentValidation;

namespace Comuki.Engine.Orchestration.Application.MergeQueue.BatchMerge;

/// <summary>
/// Structural validation of <see cref="MergeMergeBatchCommand"/>:
/// non-empty batch id. The domain factory re-enforces the
/// InProgress precondition.
/// </summary>
public sealed class MergeMergeBatchValidator : AbstractValidator<MergeMergeBatchCommand>
{
    /// <summary>Rules.</summary>
    public MergeMergeBatchValidator()
    {
        RuleFor(static command => command.BatchId)
            .NotEqual(Guid.Empty);
    }
}
