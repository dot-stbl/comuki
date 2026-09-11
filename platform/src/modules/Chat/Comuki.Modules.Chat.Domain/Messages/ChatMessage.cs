using Comuki.Modules.Chat.Domain.Ids;

namespace Comuki.Modules.Chat.Domain.Messages;

/// <summary>
/// One entry of the chat transcript. Append-only: the table is the session
/// history AND the audit journal (role=system rows record what was fed to
/// the brain). Graph state does not live here — it lives in the checkpoint.
/// <para>
/// A row carries its rich shape in <see cref="PartsJson"/> (the ordered
/// message parts) and what it cost in <see cref="MetaJson"/>; both are
/// optional. <see cref="Content"/> is never optional — it is the flat text
/// projection of the parts, so the memory digest, search and any client
/// that predates parts keep reading one column.
/// </para>
/// </summary>
public sealed class ChatMessage
{
    /// <summary>Upper bound of <see cref="Content"/> — mirrored by the column and the message validator.</summary>
    public const int MaxContentLength = 8000;

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

    /// <summary>
    /// The ordered message parts as a JSON array (<c>parts</c> jsonb);
    /// null on a row that was only ever flat text.
    /// </summary>
    public string? PartsJson { get; private set; }

    /// <summary>
    /// What producing the row cost as a JSON object (<c>meta</c> jsonb) —
    /// model, tokens, cost, latency, stop reason; null when unknown.
    /// </summary>
    public string? MetaJson { get; private set; }

    /// <summary>When the message was appended.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Appends a message.</summary>
    /// <param name="sessionId"></param>
    /// <param name="role"></param>
    /// <param name="content">Flat text projection of the parts; the one column every reader can rely on.</param>
    /// <param name="toolName"></param>
    /// <param name="now"></param>
    /// <param name="partsJson">Serialized part array; null for a flat-text-only row.</param>
    /// <param name="metaJson">Serialized per-message metadata; null when unknown.</param>
    /// <exception cref="ArgumentException"></exception>
    public static ChatMessage Create(
        ChatSessionId sessionId,
        ChatMessageRole role,
        string content,
        string? toolName,
        DateTimeOffset now,
        string? partsJson = null,
        string? metaJson = null)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("message content must not be empty", nameof(content));
        }

        if (role == ChatMessageRole.Tool && string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException("tool messages require a tool name", nameof(toolName));
        }

        if (content.Length > MaxContentLength)
        {
            throw new ArgumentException(
                $"message content must be {MaxContentLength} characters or fewer",
                nameof(content));
        }

        var id = Guid.CreateVersion7();
        return new ChatMessage
        {
            Id = id,
            SessionId = sessionId,
            Role = role,
            Content = content,
            ToolName = role == ChatMessageRole.Tool ? toolName : null,
            PartsJson = partsJson,
            MetaJson = metaJson,
            CreatedAt = now,
        };
    }
}
