using FluentValidation;

namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>
/// Structural validation of <see cref="CreateMergeBatchCommand"/>:
/// bounded name length (mirrors the EF column), non-empty PR URL list,
/// bounded URL length per entry, and a cap on the array size to keep
/// writes predictable. Domain factory throws on invariant violations
/// (name / list / url emptiness) — these rules catch them earlier
/// with bounded-length feedback. The per-verb action commands have
/// their own validators under <see cref="BatchClaim"/>,
/// <see cref="BatchMerge"/>, <see cref="BatchAbandon"/>.
/// </summary>
public sealed class MergeBatchValidator : AbstractValidator<CreateMergeBatchCommand>
{
    /// <summary>Maximum batch-name length we accept on create — mirrors the EF column.</summary>
    public const int MaxNameLength = 256;

    /// <summary>Maximum PR URL length we accept on create — mirrors the EF column.</summary>
    public const int MaxPullRequestUrlLength = 1024;

    /// <summary>Maximum number of PR URLs in one batch — caps the array size to keep writes predictable.</summary>
    public const int MaxUrlsPerBatch = 32;

    /// <summary>Rules.</summary>
    public MergeBatchValidator()
    {
        RuleFor(static command => command.Name)
            .NotEmpty()
            .MaximumLength(MaxNameLength);

        RuleFor(static command => command.PullRequestUrls)
            .NotEmpty()
            .Must(static urls => urls.Count <= MaxUrlsPerBatch)
            .WithMessage($"at most {MaxUrlsPerBatch} pull request urls per batch");

        RuleFor(static command => command.PullRequestUrls)
            .ForEach(static rule => rule.NotEmpty().MaximumLength(MaxPullRequestUrlLength));
    }
}
