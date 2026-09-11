using Comuki.Modules.Chat.Domain.Ids;
using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Shared.Contracts.Chat;

namespace Comuki.Modules.Chat.Application.Sessions;

/// <summary>
/// The one place a transcript row is built from message parts. It
/// serializes the parts into the <c>parts</c> column AND writes the flat
/// text projection into <c>content</c> — nothing else in the module is
/// allowed to compose <c>content</c> by hand, which is what kept the plan
/// JSON glued to the reply with a blank line.
/// </summary>
public static class ChatTranscriptRow
{
    /// <summary>Builds one transcript row out of its parts.</summary>
    /// <param name="sessionId">Session the row belongs to.</param>
    /// <param name="role">Who produced the row.</param>
    /// <param name="parts">Ordered parts; the flat projection is derived from them.</param>
    /// <param name="now">Journal stamp.</param>
    /// <param name="toolName">Tool name for role=tool rows; null otherwise.</param>
    /// <param name="meta">What producing the row cost; null when unknown.</param>
    public static ChatMessage Of(
        ChatSessionId sessionId,
        ChatMessageRole role,
        IReadOnlyList<MessagePart> parts,
        DateTimeOffset now,
        string? toolName = null,
        ChatMessageMeta? meta = null)
    {
        return ChatMessage.Create(
            sessionId,
            role,
            MessagePartText.Clamp(MessagePartText.Flatten(parts), ChatMessage.MaxContentLength),
            toolName,
            now,
            MessagePartsJson.Serialize(parts),
            meta is null ? null : MessagePartsJson.SerializeMeta(meta));
    }
}
