using FluentValidation;

namespace Comuki.Engine.Orchestration.Application.MergeQueue.BatchAbandon;

/// <summary>
/// Structural validation of <see cref="AbandonMergeBatchCommand"/>:
/// non-empty batch id, non-empty bounded reason. The domain factory
/// re-enforces both at the store.
/// </summary>
public sealed class AbandonMergeBatchValidator : AbstractValidator<AbandonMergeBatchCommand>
{
    /// <summary>Maximum abandon-reason length we accept.</summary>
    public const int MaxReasonLength = 1024;

    /// <summary>Rules.</summary>
    public AbandonMergeBatchValidator()
    {
        RuleFor(static command => command.BatchId)
            .NotEqual(Guid.Empty);

        RuleFor(static command => command.Reason)
            .NotEmpty()
            .MaximumLength(MaxReasonLength);
    }
}
