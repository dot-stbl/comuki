namespace Comuki.Modules.Memory.Domain.Chat;

/// <summary>
/// One chat message of a session. Sessions are identified by their
/// string id (the Voluta thread id); the full conversation history is
/// replayable from this table days later.
/// </summary>
public sealed class ChatMessage
{
    internal ChatMessage()
    {
    }

    /// <summary>Message id (UUIDv7, client-side).</summary>
    public Guid Id { get; private set; }

    /// <summary>The chat session (Voluta thread) the message belongs to.</summary>
    public string SessionId { get; private set; } = string.Empty;

    /// <summary>Author role — <c>user</c> / <c>assistant</c> / <c>tool</c> / <c>system</c>.</summary>
    public string Role { get; private set; } = string.Empty;

    /// <summary>Message body.</summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>When the message was recorded.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Creates a message row.</summary>
    /// <param name="sessionId"></param>
    /// <param name="role"></param>
    /// <param name="content"></param>
    /// <param name="now"></param>
    /// <exception cref="ArgumentException"></exception>
    public static ChatMessage Create(string sessionId, string role, string content, DateTimeOffset now)
    {
        return string.IsNullOrWhiteSpace(sessionId)
            ? throw new ArgumentException("session id must not be empty", nameof(sessionId))
            : string.IsNullOrWhiteSpace(role)
            ? throw new ArgumentException("role must not be empty", nameof(role))
            : string.IsNullOrWhiteSpace(content)
            ? throw new ArgumentException("content must not be empty", nameof(content))
            : new ChatMessage
            {
                Id = Guid.CreateVersion7(),
                SessionId = sessionId.Trim(),
                Role = role.Trim().ToLowerInvariant(),
                Content = content,
                CreatedAt = now,
            };
    }
}
