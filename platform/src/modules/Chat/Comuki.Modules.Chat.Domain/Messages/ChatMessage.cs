using Comuki.Modules.Chat.Domain.Ids;

namespace Comuki.Modules.Chat.Domain.Messages;

/// <summary>
/// One entry of the chat transcript. Append-only: the table is the session
/// history AND the audit journal (role=system rows record what was fed to
/// the brain). Graph state does not live here — it lives in the checkpoint.
/// </summary>
public sealed class ChatMessage
{
    internal ChatMessage()
    {
    }

    /// <summary>Message id (UUIDv7, client-side).</summary>
    public Guid Id { get; private set; }

    /// <summary>The session this message belongs to.</summary>
    public ChatSessionId SessionId { get; private set; }

    /// <summary>Who produced the message.</summary>
    public ChatMessageRole Role { get; private set; }

    /// <summary>Message text (markdown for assistant, plain for user; digest text for system).</summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>Tool name for role=tool rows; null otherwise.</summary>
    public string? ToolName { get; private set; }

    /// <summary>When the message was appended.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Appends a message.</summary>
    /// <param name="sessionId"></param>
    /// <param name="role"></param>
    /// <param name="content"></param>
    /// <param name="toolName"></param>
    /// <param name="now"></param>
    /// <exception cref="ArgumentException"></exception>
    public static ChatMessage Create(
        ChatSessionId sessionId,
        ChatMessageRole role,
        string content,
        string? toolName,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("message content must not be empty", nameof(content));
        }

        if (role == ChatMessageRole.Tool && string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException("tool messages require a tool name", nameof(toolName));
        }

        var id = Guid.CreateVersion7();
        return new ChatMessage
        {
            Id = id,
            SessionId = sessionId,
            Role = role,
            Content = content,
            ToolName = role == ChatMessageRole.Tool ? toolName : null,
            CreatedAt = now,
        };
    }
}
