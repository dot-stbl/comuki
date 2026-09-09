using FluentValidation;

namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>
/// Structural validation of the merge-batch commands: bounded name
/// length (mirrors the EF column), non-empty PR URL list, bounded URL
/// length per entry, and the abandon-reason length. Domain factory
/// throws on invariant violations (name / list / url emptiness) — these
/// rules catch them earlier with bounded-length feedback.
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

/// <summary>Update-side structural validation: reason on Abandon.</summary>
public sealed class UpdateMergeBatchValidator : AbstractValidator<UpdateMergeBatchCommand>
{
    /// <summary>Maximum abandon-reason length we accept.</summary>
    public const int MaxReasonLength = 1024;

    /// <summary>Rules.</summary>
    public UpdateMergeBatchValidator()
    {
        RuleFor(static command => command.BatchId)
            .NotEqual(Guid.Empty);

        RuleFor(static command => command.Reason)
            .NotEmpty()
            .MaximumLength(MaxReasonLength)
            .When(static command => command.Action is MergeBatchAction.Abandon);
    }
}
