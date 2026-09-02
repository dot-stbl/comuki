using FluentValidation;

namespace Comuki.Modules.Projects.Application.Settings.Update;

/// <summary>Structural validation of <see cref="UpdateSettingsCommand"/>; the version race is handled by the store.</summary>
public sealed class UpdateSettingsValidator : AbstractValidator<UpdateSettingsCommand>
{
    /// <summary>Rules: presented version positive, scale ranges, min_idle ≤ max_concurrent, idle TTL bounds.</summary>
    public UpdateSettingsValidator()
    {
        _ = RuleFor(static command => command.Version)
            .GreaterThanOrEqualTo(1);

        _ = RuleFor(static command => command.MinIdle)
            .InclusiveBetween(0, 1000);

        _ = RuleFor(static command => command.MaxConcurrent)
            .InclusiveBetween(1, 1000);

        _ = RuleFor(static command => command)
            .Must(static command => command.MinIdle <= command.MaxConcurrent)
            .WithMessage("min_idle must not exceed max_concurrent");

        _ = RuleFor(static command => command.IdleTtlSeconds)
            .Must(static idleTtlSeconds => idleTtlSeconds is null or (>= 30 and <= 86400))
            .WithMessage("idle_ttl_seconds must be between 30 and 86400, or null for the engine default");

        _ = RuleFor(static command => command.SoftBudgetUsdMicros)
            .Must(static soft => soft is null or >= 0)
            .WithMessage("soft_budget_usd_micros must be >= 0 or null");

        _ = RuleFor(static command => command.HardBudgetUsdMicros)
            .Must(static hard => hard is null or >= 0)
            .WithMessage("hard_budget_usd_micros must be >= 0 or null");

        _ = RuleFor(static command => command)
            .Must(static command =>
                command.SoftBudgetUsdMicros is null
                || command.HardBudgetUsdMicros is null
                || command.SoftBudgetUsdMicros <= command.HardBudgetUsdMicros)
            .WithMessage("soft_budget_usd_micros must not exceed hard_budget_usd_micros");
    }
}
