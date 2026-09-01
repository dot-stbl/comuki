using Comuki.Modules.Chat.Application.Paging;
using Comuki.Modules.Chat.Domain.Ids;
using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Modules.Chat.Domain.Sessions;

namespace Comuki.Modules.Chat.Application.Ports;

/// <summary>
/// Persistence port for the chat transcript and sessions. Implemented by the
/// module infrastructure over its DbContext; graph nodes resolve it too, so
/// the implementation must be singleton-safe (context per call, not per
/// scope).
/// </summary>
public interface IChatSessionStore
{
    /// <summary>Appends a new session.</summary>
    /// <param name="session"></param>
    /// <param name="cancellationToken"></param>
    public Task AddAsync(ChatSession session, CancellationToken cancellationToken = default);

    /// <summary>Finds a session by id; null when unknown.</summary>
    /// <param name="sessionId"></param>
    /// <param name="cancellationToken"></param>
    public Task<ChatSession?> FindByIdAsync(ChatSessionId sessionId, CancellationToken cancellationToken = default);

    /// <summary>Newest sessions of one subject, newest first.</summary>
    /// <param name="subjectId">Owning subject.</param>
    /// <param name="limit">Maximum rows to return.</param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<ChatSession>> ListForSubjectAsync(Guid subjectId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Persists a changed session (title, status, activity stamp).</summary>
    /// <param name="session"></param>
    /// <param name="cancellationToken"></param>
    public Task SaveAsync(ChatSession session, CancellationToken cancellationToken = default);

    /// <summary>Appends one transcript row.</summary>
    /// <param name="message"></param>
    /// <param name="cancellationToken"></param>
    public Task AppendAsync(ChatMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the newest <paramref name="limit"/> messages of a session,
    /// oldest first (history window for the brain context).
    /// </summary>
    /// <param name="sessionId"></param>
    /// <param name="limit"></param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<ChatMessage>> ReadRecentAsync(ChatSessionId sessionId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Reads a page of the transcript, oldest first; pages are 1-based.</summary>
    /// <param name="sessionId"></param>
    /// <param name="page"></param>
    /// <param name="pageSize"></param>
    /// <param name="cancellationToken"></param>
    public Task<ChatMessagePage> ReadPageAsync(ChatSessionId sessionId, int page, int pageSize, CancellationToken cancellationToken = default);
}
