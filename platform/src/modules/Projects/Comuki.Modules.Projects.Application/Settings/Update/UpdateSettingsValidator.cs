using System.Text.Json;
using Comuki.Modules.Projects.Domain.Settings;
using FluentValidation;

namespace Comuki.Modules.Projects.Application.Settings.Update;

/// <summary>
/// Structural validation of <see cref="UpdateSettingsCommand"/>; the
/// version race is handled by the store, the JSON map shape by these
/// rules. Per-field errors map to 400 with the field name
/// (<c>customDomainTypesJson</c>) in the response.
/// </summary>
public sealed class UpdateSettingsValidator : AbstractValidator<UpdateSettingsCommand>
{
    /// <summary>Maximum JSON size we accept on PUT — 8 KiB keeps one map per project bounded.</summary>
    public const int MaxCustomDomainTypesJsonLength = 8192;

    /// <summary>Rules: presented version positive, scale ranges, min_idle ≤ max_concurrent, idle TTL bounds, JSON map shape.</summary>
    public UpdateSettingsValidator()
    {
        RuleFor(static command => command.Version)
            .GreaterThanOrEqualTo(1);

        RuleFor(static command => command.MinIdle)
            .InclusiveBetween(0, 1000);

        RuleFor(static command => command.MaxConcurrent)
            .InclusiveBetween(1, 1000);

        RuleFor(static command => command)
            .Must(static command => command.MinIdle <= command.MaxConcurrent)
            .WithMessage("min_idle must not exceed max_concurrent");

        RuleFor(static command => command.IdleTtlSeconds)
            .Must(static idleTtlSeconds => idleTtlSeconds is null or (>= 30 and <= 86400))
            .WithMessage("idle_ttl_seconds must be between 30 and 86400, or null for the engine default");

        RuleFor(static command => command.SoftBudgetUsdMicros)
            .Must(static soft => soft is null or >= 0)
            .WithMessage("soft_budget_usd_micros must be >= 0 or null");

        RuleFor(static command => command.HardBudgetUsdMicros)
            .Must(static hard => hard is null or >= 0)
            .WithMessage("hard_budget_usd_micros must be >= 0 or null");

        RuleFor(static command => command)
            .Must(static command =>
                command.SoftBudgetUsdMicros is null
                || command.HardBudgetUsdMicros is null
                || command.SoftBudgetUsdMicros <= command.HardBudgetUsdMicros)
            .WithMessage("soft_budget_usd_micros must not exceed hard_budget_usd_micros");

        // Domain-type routing — bounds + JSON shape.
        RuleFor(static command => command.CustomDomainTypesJson)
            .Must(static json => json is null || json.Length <= MaxCustomDomainTypesJsonLength)
            .WithMessage($"customDomainTypesJson must be at most {MaxCustomDomainTypesJsonLength} characters");

        RuleFor(static command => command.CustomDomainTypesJson)
            .Must(BeValidDomainMapJsonOrNull)
            .WithMessage(
                "customDomainTypesJson must be null or a JSON object whose values are non-empty strings");

        RuleFor(static command => command.CustomDomainTypesJson)
            .NotEmpty()
            .When(static command => command.DomainType == ProjectDomainType.Custom)
            .WithMessage("customDomainTypesJson is required when domainType is Custom");
    }

    private static bool BeValidDomainMapJsonOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web);
            if (parsed is null)
            {
                return false;
            }

            // every value must be a non-empty profile key (controller names map to the
            // control-plane profile file stem)
            return parsed.Values.All(static value => !string.IsNullOrWhiteSpace(value));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
