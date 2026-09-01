using Comuki.Modules.Chat.Domain.Ids;

namespace Comuki.Modules.Chat.Application.Sessions;

/// <summary>The referenced chat session does not exist (or belongs to another subject); maps to HTTP 404.</summary>
/// <param name="sessionId"></param>
public sealed class ChatSessionNotFoundException(ChatSessionId sessionId)
    : Exception($"chat session '{sessionId}' not found")
{
    /// <summary>Session that was looked up.</summary>
    public ChatSessionId SessionId { get; } = sessionId;
}
