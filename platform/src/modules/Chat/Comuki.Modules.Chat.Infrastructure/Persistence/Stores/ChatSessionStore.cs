using Comuki.Modules.Chat.Application.Paging;
using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Domain.Ids;
using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Modules.Chat.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Chat.Infrastructure.Persistence.Stores;

/// <summary>
/// EF implementation of the chat transcript store. One context per call
/// from the factory — the store is a singleton, safe for graph nodes and
/// request handlers alike.
/// </summary>
/// <param name="factory">Context factory over the chat schema.</param>
public sealed class ChatSessionStore(IDbContextFactory<ChatDbContext> factory) : IChatSessionStore
{

    /// <inheritdoc />
    public async Task AddAsync(ChatSession session, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ChatSession?> FindByIdAsync(ChatSessionId sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Sessions.AsNoTracking()
            .SingleOrDefaultAsync(session => session.Id == sessionId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatSession>> ListForSubjectAsync(Guid subjectId, int limit, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var sessions = await db.Sessions.AsNoTracking()
            .Where(session => session.SubjectId == subjectId && session.Status == ChatSessionStatus.Active)
            .OrderByDescending(session => session.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return sessions;
    }

    /// <inheritdoc />
    public async Task SaveAsync(ChatSession session, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.Sessions.Update(session);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AppendAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.Messages.Add(message);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatMessage>> ReadRecentAsync(ChatSessionId sessionId, int limit, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Messages.AsNoTracking()
            .Where(message => message.SessionId == sessionId)
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        rows.Reverse();
        return rows;
    }

    /// <inheritdoc />
    public async Task<ChatMessagePage> ReadPageAsync(ChatSessionId sessionId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var query = db.Messages.AsNoTracking()
            .Where(message => message.SessionId == sessionId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new ChatMessagePage(items, page, pageSize, total);
    }
}
