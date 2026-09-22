using FluentValidation;

namespace Comuki.Engine.Orchestration.Application.MergeQueue.BatchClaim;

/// <summary>
/// Structural validation of <see cref="ClaimMergeBatchCommand"/>:
/// non-empty batch id. The domain factory re-enforces the Pending
/// precondition.
/// </summary>
public sealed class ClaimMergeBatchValidator : AbstractValidator<ClaimMergeBatchCommand>
{
    /// <summary>Rules.</summary>
    public ClaimMergeBatchValidator()
    {
        RuleFor(static command => command.BatchId)
            .NotEqual(Guid.Empty);
    }
}
