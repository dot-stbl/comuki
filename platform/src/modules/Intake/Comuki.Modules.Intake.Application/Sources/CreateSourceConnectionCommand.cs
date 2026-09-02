using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;

namespace Comuki.Modules.Intake.Application.Sources;

/// <summary>Creates a source connection (permission <c>source:write</c>).</summary>
/// <param name="ProjectId"></param>
/// <param name="Provider">Kebab-case provider key (github | gitlab | yandex-tracker | jira).</param>
/// <param name="Name"></param>
/// <param name="SettingsJson">Provider-specific, non-secret settings.</param>
/// <param name="SecretEnvRef">Env-var name holding the webhook secret.</param>
public sealed record CreateSourceConnectionCommand(
    ProjectId ProjectId,
    string Provider,
    string Name,
    string SettingsJson,
    string SecretEnvRef);

/// <summary>Structural validation of a connection command.</summary>
public sealed class CreateSourceConnectionValidator : AbstractValidator<CreateSourceConnectionCommand>
{
    /// <summary>Rules for the connection shape.</summary>
    public CreateSourceConnectionValidator()
    {
        RuleFor(static command => command.Provider)
            .Must(static provider => TicketProviderKeys.TryParse(provider) is { } parsed
                && parsed is not TicketProvider.Native)
            .WithMessage("provider must be one of: github, gitlab, yandex-tracker, jira");

        RuleFor(static command => command.Name)
            .NotEmpty()
            .MaximumLength(128);

        RuleFor(static command => command.SettingsJson)
            .Must(BeJsonObject)
            .WithMessage("settings must be a json object")
            .MaximumLength(8192);

        RuleFor(static command => command.SecretEnvRef)
            .NotEmpty()
            .Matches("^[A-Za-z0-9_]+$")
            .WithMessage("secretEnvRef must be an environment variable name ([A-Za-z0-9_]+)");
    }

    private static bool BeJsonObject(string json)
    {
        var trimmed = json.Trim();
        return trimmed.StartsWith('{') && trimmed.EndsWith('}');
    }
}
