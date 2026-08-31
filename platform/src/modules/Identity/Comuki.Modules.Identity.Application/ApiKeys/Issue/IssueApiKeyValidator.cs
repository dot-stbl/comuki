using FluentValidation;

namespace Comuki.Modules.Identity.Application.ApiKeys.Issue;

/// <summary>Structural validation of <see cref="IssueApiKeyCommand"/>.</summary>
public sealed class IssueApiKeyValidator : AbstractValidator<IssueApiKeyCommand>
{
    /// <summary>Rules: known user id, non-empty bounded label.</summary>
    public IssueApiKeyValidator()
    {
        _ = RuleFor(static command => command.UserId.Value)
            .Must(static id => id != Guid.Empty);

        _ = RuleFor(static command => command.Name)
            .NotEmpty()
            .MaximumLength(128);
    }
}
