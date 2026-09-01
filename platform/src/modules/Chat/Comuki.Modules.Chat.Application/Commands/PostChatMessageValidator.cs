using FluentValidation;

namespace Comuki.Modules.Chat.Application.Commands;

/// <summary>Validates a posted chat message (the only user free-text input of a turn).</summary>
public sealed class PostChatMessageValidator : AbstractValidator<PostChatMessageCommand>
{
    /// <summary>Maximum message length — matches the brain task guard.</summary>
    public const int MaxMessageLength = 8000;

    /// <summary>Initializes the rules.</summary>
    public PostChatMessageValidator()
    {
        RuleFor(static command => command.Message)
            .NotEmpty()
            .WithMessage("message must not be empty or whitespace")
            .MaximumLength(MaxMessageLength)
            .WithMessage($"message must be {MaxMessageLength} characters or fewer");
    }
}
