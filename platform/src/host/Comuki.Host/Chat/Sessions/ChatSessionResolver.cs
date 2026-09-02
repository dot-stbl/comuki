using System.Security.Claims;
using Comuki.Modules.Chat.Application.Sessions;
using Comuki.Modules.Chat.Domain.Ids;
using Comuki.Modules.Chat.Domain.Sessions;

namespace Comuki.Host.Chat.Sessions;

/// <summary>
/// Principal → owned session lookup for the chat endpoints: resolves the
/// acting subject, finds the session and applies the owner check. A
/// foreign or unknown session resolves to null (the endpoints answer 404).
/// </summary>
/// <param name="sessions">Session lifecycle service.</param>
public sealed class ChatSessionResolver(ChatSessionService sessions)
{
    /// <summary>Finds the session owned by the acting principal; null when unknown or foreign.</summary>
    /// <param name="sessionId">Route session id.</param>
    /// <param name="principal">Authenticated principal.</param>
    /// <param name="cancellationToken"></param>
    public Task<ChatSession?> ResolveAsync(Guid sessionId, ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        return sessions.FindOwnedAsync(new ChatSessionId(sessionId), ChatSubjects.ResolveSubjectId(principal), cancellationToken);
    }
}
