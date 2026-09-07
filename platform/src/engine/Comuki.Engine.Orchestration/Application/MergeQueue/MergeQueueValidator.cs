using FluentValidation;

namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>
/// Structural validation of the merge-queue commands: bounded string
/// lengths (the EF column constraints re-enforce them at the store),
/// non-empty branch / URL, non-empty operator id on Claim / Abandon.
/// Domain factory throws on invariant violations (operator id check
/// on Claim, reason check on Abandon) — these rules catch them earlier.
/// </summary>
public sealed class MergeQueueValidator : AbstractValidator<EnqueueMergeRequestCommand>
{
    /// <summary>Maximum branch-name length we accept on enqueue — mirrors the EF column.</summary>
    public const int MaxBranchNameLength = 256;

    /// <summary>Maximum PR URL length we accept on enqueue.</summary>
    public const int MaxPullRequestUrlLength = 1024;

    /// <summary>Maximum notes length.</summary>
    public const int MaxNotesLength = 4096;

    /// <summary>Rules.</summary>
    public MergeQueueValidator()
    {
        _ = RuleFor(static command => command.BranchName)
            .NotEmpty()
            .MaximumLength(MaxBranchNameLength);

        _ = RuleFor(static command => command.PullRequestUrl)
            .NotEmpty()
            .MaximumLength(MaxPullRequestUrlLength);

        _ = RuleFor(static command => command.Notes)
            .MaximumLength(MaxNotesLength);
    }
}

/// <summary>Update-side structural validation: operator id on Claim / Abandon; reason on Abandon.</summary>
public sealed class UpdateMergeQueueValidator : AbstractValidator<UpdateMergeQueueCommand>
{
    /// <summary>Maximum operator id length we accept.</summary>
    public const int MaxOperatorIdLength = 128;

    /// <summary>Maximum abandon-reason length we accept.</summary>
    public const int MaxReasonLength = 1024;

    /// <summary>Rules.</summary>
    public UpdateMergeQueueValidator()
    {
        _ = RuleFor(static command => command.EntryId)
            .NotEqual(Guid.Empty);

        _ = RuleFor(static command => command.OperatorId)
            .NotEmpty()
            .MaximumLength(MaxOperatorIdLength)
            .When(static command => command.Action is MergeQueueAction.Claim);

        _ = RuleFor(static command => command.OperatorId)
            .NotEmpty()
            .MaximumLength(MaxOperatorIdLength)
            .When(static command => command.Action is MergeQueueAction.Abandon);

        _ = RuleFor(static command => command.Reason)
            .NotEmpty()
            .MaximumLength(MaxReasonLength)
            .When(static command => command.Action is MergeQueueAction.Abandon);

        _ = RuleFor(static command => command.Notes)
            .MaximumLength(MergeQueueValidator.MaxNotesLength)
            .When(static command => command.Action is MergeQueueAction.Annotate);
    }
}
