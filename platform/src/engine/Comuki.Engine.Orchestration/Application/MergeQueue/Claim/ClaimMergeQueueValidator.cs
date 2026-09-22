using FluentValidation;

namespace Comuki.Engine.Orchestration.Application.MergeQueue.Claim;

/// <summary>
/// Structural validation of <see cref="ClaimMergeQueueCommand"/>:
/// non-empty entry id, non-empty bounded operator id. Domain factory
/// re-enforces both at the store.
/// </summary>
public sealed class ClaimMergeQueueValidator : AbstractValidator<ClaimMergeQueueCommand>
{
    /// <summary>Maximum operator id length we accept.</summary>
    public const int MaxOperatorIdLength = 128;

    /// <summary>Rules.</summary>
    public ClaimMergeQueueValidator()
    {
        RuleFor(static command => command.EntryId)
            .NotEqual(Guid.Empty);

        RuleFor(static command => command.OperatorId)
            .NotEmpty()
            .MaximumLength(MaxOperatorIdLength);
    }
}
