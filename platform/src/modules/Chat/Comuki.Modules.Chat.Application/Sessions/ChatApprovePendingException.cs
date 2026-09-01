using Comuki.Modules.Chat.Domain.Ids;

namespace Comuki.Modules.Chat.Application.Sessions;

/// <summary>
/// The session has a pending approve interrupt, so a new turn or a second
/// approve would race the graph state; maps to HTTP 409. The caller must
/// resolve <c>/approve</c> first.
/// </summary>
/// <param name="sessionId"></param>
public sealed class ChatApprovePendingException(ChatSessionId sessionId)
    : Exception($"chat session '{sessionId}' is waiting for a plan approve/reject decision")
{
    /// <summary>Session that is interrupted.</summary>
    public ChatSessionId SessionId { get; } = sessionId;
}
