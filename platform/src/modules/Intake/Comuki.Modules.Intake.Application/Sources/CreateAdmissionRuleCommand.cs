using Comuki.Shared.Kernel.Ids;
using FluentValidation;

namespace Comuki.Modules.Intake.Application.Sources;

/// <summary>Creates an admission rule (permission <c>source:write</c>).</summary>
/// <param name="ProjectId"></param>
/// <param name="Mode">watch | inbox.</param>
/// <param name="FilterJson">Admission filter: {"labelsAny": [...], "projects": [...]}.</param>
public sealed record CreateAdmissionRuleCommand(ProjectId ProjectId, string Mode, string FilterJson);

/// <summary>Structural validation of an admission rule command.</summary>
public sealed class CreateAdmissionRuleValidator : AbstractValidator<CreateAdmissionRuleCommand>
{
    /// <summary>Rules for the admission rule shape.</summary>
    public CreateAdmissionRuleValidator()
    {
        RuleFor(static command => command.Mode)
            .Must(static mode => mode is "watch" or "inbox")
            .WithMessage("mode must be 'watch' or 'inbox'");

        RuleFor(static command => command.FilterJson)
            .Must(BeJsonObject)
            .WithMessage("filter must be a json object")
            .MaximumLength(8192);
    }

    private static bool BeJsonObject(string json)
    {
        var trimmed = json.Trim();
        return trimmed.StartsWith('{') && trimmed.EndsWith('}');
    }
}
