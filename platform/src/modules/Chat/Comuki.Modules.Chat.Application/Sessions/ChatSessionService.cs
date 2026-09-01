using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Domain.Ids;
using Comuki.Modules.Chat.Domain.Sessions;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Chat.Application.Sessions;

/// <summary>
/// Session lifecycle service: creation, owner-checked reads and the
/// subject's recent sessions. Sessions are soft-scoped by subject — another
/// subject's session reads as missing (404), not forbidden.
/// </summary>
/// <param name="store">Session persistence.</param>
/// <param name="clock">Time source for lifecycle stamps.</param>
public sealed class ChatSessionService(IChatSessionStore store, TimeProvider clock)
{
    /// <summary>How many sessions a listing returns by default.</summary>
    public const int DefaultListLimit = 50;

    /// <summary>Creates an active session for the subject.</summary>
    /// <param name="subjectId">Acting subject (user or api key).</param>
    /// <param name="projectId">Optional project scope.</param>
    /// <param name="title">Optional title; defaults to a placeholder.</param>
    /// <param name="cancellationToken"></param>
    public async Task<ChatSession> CreateAsync(Guid subjectId, ProjectId? projectId, string? title, CancellationToken cancellationToken = default)
    {
        var session = ChatSession.Create(projectId, subjectId, title ?? string.Empty, clock.GetUtcNow());
        await store.AddAsync(session, cancellationToken);
        return session;
    }

    /// <summary>Finds a session owned by the subject; null when unknown or foreign.</summary>
    /// <param name="sessionId"></param>
    /// <param name="subjectId"></param>
    /// <param name="cancellationToken"></param>
    public async Task<ChatSession?> FindOwnedAsync(ChatSessionId sessionId, Guid subjectId, CancellationToken cancellationToken = default)
    {
        var session = await store.FindByIdAsync(sessionId, cancellationToken);
        return session is { Status: ChatSessionStatus.Active } && session.SubjectId == subjectId ? session : null;
    }

    /// <summary>The subject's recent active sessions, newest first.</summary>
    /// <param name="subjectId"></param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<ChatSession>> ListRecentAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        return store.ListForSubjectAsync(subjectId, DefaultListLimit, cancellationToken);
    }
}
