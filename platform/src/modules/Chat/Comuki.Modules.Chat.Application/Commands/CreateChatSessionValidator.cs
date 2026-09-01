using FluentValidation;

namespace Comuki.Modules.Chat.Application.Commands;

/// <summary>Validates a session-creation command (title is the only free-text field).</summary>
public sealed class CreateChatSessionValidator : AbstractValidator<CreateChatSessionCommand>
{
    /// <summary>Maximum session title length.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>Initializes the rules.</summary>
    public CreateChatSessionValidator()
    {
        RuleFor(static command => command.Title)
            .MaximumLength(MaxTitleLength)
            .When(static command => !string.IsNullOrWhiteSpace(command.Title), ApplyConditionTo.CurrentValidator)
            .WithMessage($"session title must be {MaxTitleLength} characters or fewer");
    }
}
