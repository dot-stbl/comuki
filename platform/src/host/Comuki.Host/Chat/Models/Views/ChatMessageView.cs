using Comuki.Modules.Chat.Domain.Messages;

namespace Comuki.Host.Chat.Models.Views;

/// <summary>Transcript row read model.</summary>
public sealed class ChatMessageView
{
    /// <summary>Message id (uuidv7).</summary>
    public required Guid Id { get; init; }

    /// <summary>Role wire string: user | assistant | system | tool.</summary>
    public required string Role { get; init; }

    /// <summary>Message text.</summary>
    public required string Content { get; init; }

    /// <summary>Tool name for role=tool rows; null otherwise.</summary>
    public string? ToolName { get; init; }

    /// <summary>When the row was appended.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Maps the domain row.</summary>
    /// <param name="message"></param>
    public static ChatMessageView Of(ChatMessage message)
    {
        return new ChatMessageView
        {
            Id = message.Id,
            Role = message.Role.ToString().ToLowerInvariant(),
            Content = message.Content,
            ToolName = message.ToolName,
            CreatedAt = message.CreatedAt,
        };
    }
}
