using Comuki.Modules.Chat.Domain.Ids;
using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Chat.Application.Sessions;

/// <summary>
/// The session has a pending approve interrupt, so a new turn or a second
/// approve would race the graph state; maps to HTTP 409. The caller must
/// resolve <c>/approve</c> first.
/// </summary>
/// <param name="sessionId"></param>
public sealed class ChatApprovePendingException(ChatSessionId sessionId)
    : DomainException(ErrorCode, $"chat session '{sessionId}' is waiting for a plan approve/reject decision")
{
    private const string ErrorCode = "chat.approve_pending";

    /// <summary>Session that is interrupted.</summary>
    public ChatSessionId SessionId { get; } = sessionId;
}
