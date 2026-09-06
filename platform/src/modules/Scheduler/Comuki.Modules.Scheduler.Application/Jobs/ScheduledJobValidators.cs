using FluentValidation;

namespace Comuki.Modules.Scheduler.Application.Jobs;

/// <summary>
/// Structural validation for the create command: non-empty cron (the
/// shape is re-parsed in the domain factory — this validator only
/// catches empty / oversized inputs before the domain layer runs).
/// </summary>
public sealed class CreateScheduledJobValidator : AbstractValidator<CreateScheduledJobCommand>
{
    /// <summary>Cron expression max length — 256 chars covers any sane 5-field expression with steps / lists.</summary>
    public const int CronExpressionMaxLength = 256;

    /// <summary>Profile key max length — kebab-case key into the control plane catalog.</summary>
    public const int ProfileKeyMaxLength = 64;

    /// <summary>Brief payload max length — same envelope as the intake brief (worker brief jsonb).</summary>
    public const int BriefJsonMaxLength = 32768;

    /// <inheritdoc />
    public CreateScheduledJobValidator()
    {
        RuleFor(static command => command.CronExpression)
            .NotEmpty()
            .MaximumLength(CronExpressionMaxLength);

        RuleFor(static command => command.ProfileKey)
            .NotEmpty()
            .MaximumLength(ProfileKeyMaxLength)
            .Matches("^[a-z0-9][a-z0-9-]*$")
            .WithMessage("profile key must be kebab-case (lowercase letters, digits, dash, must start with letter or digit)");

        RuleFor(static command => command.BriefJson)
            .NotEmpty()
            .MaximumLength(BriefJsonMaxLength);
    }
}

/// <summary>Sanity-checks the cron expression eagerly so callers see a typed exception with a stable code.</summary>
public sealed class UpdateScheduledJobValidator : AbstractValidator<UpdateScheduledJobCommand>
{
    /// <inheritdoc />
    public UpdateScheduledJobValidator()
    {
        RuleFor(static command => command.CronExpression)
            .MaximumLength(CreateScheduledJobValidator.CronExpressionMaxLength)
            .When(static command => command.CronExpression is { Length: > 0 }, ApplyConditionTo.CurrentValidator);

        RuleFor(static command => command.ProfileKey)
            .MaximumLength(CreateScheduledJobValidator.ProfileKeyMaxLength)
            .Matches("^[a-z0-9][a-z0-9-]*$")
            .When(static command => command.ProfileKey is { Length: > 0 }, ApplyConditionTo.CurrentValidator)
            .WithMessage("profile key must be kebab-case (lowercase letters, digits, dash, must start with letter or digit)");
    }
}
