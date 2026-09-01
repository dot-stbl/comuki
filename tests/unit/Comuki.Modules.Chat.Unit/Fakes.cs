using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Domain.Ids;
using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Shared.Contracts.Brain;

namespace Comuki.Modules.Chat.Unit;

/// <summary>
/// Scriptable brain port: records every request, answers plan invocations
/// with a fixed single-item plan and chat invocations with a fixed reply.
/// </summary>
public sealed class FakeBrainClient : IBrainClient
{
    public const string PlanJson =
        /*lang=json,strict*/
        """{"summary":"stub plan","nodes":[{"id":"n1","title":"Do it","profileKey":"implement","brief":"stub brief"}],"edges":[]}""";

    public List<BrainRequest> Requests { get; } = [];

    /// <inheritdoc />
    public Task<BrainReply> InvokeAsync(BrainRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        var reply = request.Kind == "plan"
            ? new BrainReply([], PlanJson)
            : new BrainReply([], "brain says: " + request.Task);
        return Task.FromResult(reply);
    }
}

/// <summary>Digest port returning a fixed text so digest journaling is observable.</summary>
public sealed class FakeMemoryDigest(string digest) : Shared.Contracts.Memory.IMemoryDigest
{
    public const string DefaultDigest = "prefers tabs over spaces; deploy on k3s";

    public List<Shared.Contracts.Memory.MemoryDigestRequest> Requests { get; } = [];

    /// <inheritdoc />
    public Task<string> BuildDigestAsync(
        Shared.Contracts.Memory.MemoryDigestRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(digest);
    }
}

/// <summary>In-memory transcript + session store (the only store the turn services need).</summary>
public sealed class FakeChatSessionStore : IChatSessionStore
{
    private readonly List<Domain.Sessions.ChatSession> sessions = [];

    public List<ChatMessage> Messages { get; } = [];

    /// <inheritdoc />
    public Task AddAsync(
        Domain.Sessions.ChatSession session,
        CancellationToken cancellationToken = default)
    {
        sessions.Add(session);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Domain.Sessions.ChatSession?> FindByIdAsync(
        ChatSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(sessions.SingleOrDefault(session => session.Id == sessionId));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Domain.Sessions.ChatSession>> ListForSubjectAsync(
        Guid subjectId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Domain.Sessions.ChatSession> recent =
        [
            .. sessions
                .Where(session => session.SubjectId == subjectId)
                .OrderByDescending(session => session.UpdatedAt)
                .Take(limit),
        ];
        return Task.FromResult(recent);
    }

    /// <inheritdoc />
    public Task SaveAsync(
        Domain.Sessions.ChatSession session,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AppendAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChatMessage>> ReadRecentAsync(
        ChatSessionId sessionId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChatMessage> recent =
        [
            .. Messages
                .Where(message => message.SessionId == sessionId)
                .OrderBy(message => message.CreatedAt),
        ];
        return Task.FromResult(recent);
    }

    /// <inheritdoc />
    public Task<Application.Paging.ChatMessagePage> ReadPageAsync(
        ChatSessionId sessionId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var all = Messages.Where(message => message.SessionId == sessionId).OrderBy(message => message.CreatedAt).ToList();
        return Task.FromResult(new Application.Paging.ChatMessagePage(
            [.. all.Skip((page - 1) * pageSize).Take(pageSize)],
            page,
            pageSize,
            all.Count));
    }
}

/// <summary>Recording tool executor: answers create_ticket with a fixed run id, everything else with a failure code.</summary>
public sealed class FakeChatToolExecutor : IChatToolExecutor
{
    public const string RunId = "01234567-89ab-cdef-0123-456789abcdef";

    public List<ChatToolCall> Calls { get; } = [];

    /// <inheritdoc />
    public Task<ChatToolResult> ExecuteAsync(ChatToolCall call, CancellationToken cancellationToken = default)
    {
        Calls.Add(call);
        var result = call.Name switch
        {
            "create_ticket" => new ChatToolResult(
                true,
                /*lang=json,strict*/
                $$"""{"runId":"{{RunId}}"}""",
                null,
                NotImplemented: false),
            _ => new ChatToolResult(false, "{}", "chat.tool_unknown:" + call.Name, NotImplemented: false),
        };
        return Task.FromResult(result);
    }
}
