using FluentValidation;

namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>
/// Structural validation of <see cref="EnqueueMergeRequestCommand"/>:
/// bounded string lengths (the EF column constraints re-enforce them
/// at the store), non-empty branch / URL, bounded notes. Domain factory
/// throws on invariant violations — these rules catch them earlier with
/// bounded-length feedback. The per-verb action commands have their own
/// validators under <see cref="Claim"/>, <see cref="Release"/>,
/// <see cref="MergeEntry"/>, <see cref="Abandon"/>, <see cref="Annotate"/>.
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
        RuleFor(static command => command.BranchName)
            .NotEmpty()
            .MaximumLength(MaxBranchNameLength);

        RuleFor(static command => command.PullRequestUrl)
            .NotEmpty()
            .MaximumLength(MaxPullRequestUrlLength);

        RuleFor(static command => command.Notes)
            .MaximumLength(MaxNotesLength);
    }
}
