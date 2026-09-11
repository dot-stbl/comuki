using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Shared.Contracts.Chat;

namespace Comuki.Host.Chat.Models.Views;

/// <summary>
/// Transcript row read model. <see cref="Parts"/> is the rich shape the
/// console renders; <see cref="Content"/> is the flat projection of the
/// same row and stays populated for every reader that predates parts.
/// </summary>
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

    /// <summary>
    /// Ordered message parts, discriminated by <c>kind</c>; null on a row
    /// written before parts existed or one whose payload no longer parses.
    /// </summary>
    public IReadOnlyList<MessagePart>? Parts { get; init; }

    /// <summary>What producing the row cost; null when unknown.</summary>
    public ChatMessageMeta? Meta { get; init; }

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
            Parts = MessagePartsJson.TryParse(message.PartsJson, out var parts) ? parts : null,
            Meta = MessagePartsJson.TryParseMeta(message.MetaJson, out var meta) ? meta : null,
            CreatedAt = message.CreatedAt,
        };
    }
}
