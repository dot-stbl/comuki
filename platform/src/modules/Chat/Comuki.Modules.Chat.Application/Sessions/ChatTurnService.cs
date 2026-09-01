using Comuki.Modules.Chat.Application.Graph;
using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Modules.Chat.Domain.Sessions;
using Voluta.Abstractions.Channels;
using Voluta.Abstractions.Runtime;
using Voluta.Graph;

namespace Comuki.Modules.Chat.Application.Sessions;

/// <summary>
/// Drives chat turns over the compiled graph: seeds the turn channels,
/// invokes (or resumes) the graph, hands the finished snapshot to the
/// journalist and stamps the session. The transcript is the audit journal —
/// the graph checkpoint carries only routing state.
/// </summary>
/// <param name="store">Transcript + session persistence.</param>
/// <param name="graph">Compiled chat graph (one thread per session).</param>
/// <param name="journalist">Snapshot → transcript journaling.</param>
/// <param name="clock">Time source for journal stamps.</param>
public sealed class ChatTurnService(
    IChatSessionStore store,
    CompiledGraph graph,
    ChatTurnJournalist journalist,
    TimeProvider clock) : IChatTurnService
{
    /// <inheritdoc />
    public async Task<ChatTurnResult> PostAsync(ChatSession session, string message, CancellationToken cancellationToken = default)
    {
        var threadId = session.Id.Value.ToString();
        var state = await graph.GetStateAsync(threadId, cancellationToken);

        if (state?.Status == GraphRunStatus.Interrupted)
        {
            throw new ChatApprovePendingException(session.Id);
        }

        await store.AppendAsync(
            ChatMessage.Create(session.Id, ChatMessageRole.User, message, toolName: null, clock.GetUtcNow()),
            cancellationToken);
        session.Touch(clock.GetUtcNow());
        await store.SaveAsync(session, cancellationToken);

        await graph.InvokeAsync(
            ChatTurnSeed.For(session, message),
            new RunOptions { ThreadId = threadId },
            cancellationToken);

        return await journalist.JournalAsync(session, clearPlan: false, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ChatTurnResult> ApproveAsync(ChatSession session, bool approved, string? reason, CancellationToken cancellationToken = default)
    {
        var threadId = session.Id.Value.ToString();
        var state = await graph.GetStateAsync(threadId, cancellationToken);

        if (state?.Status != GraphRunStatus.Interrupted)
        {
            throw new ChatApprovePendingException(session.Id);
        }

        await store.AppendAsync(
            ChatMessage.Create(
                session.Id,
                ChatMessageRole.User,
                approved ? "approve" : ("reject" + (reason is { Length: > 0 } ? ": " + reason : string.Empty)),
                toolName: null,
                clock.GetUtcNow()),
            cancellationToken);
        session.Touch(clock.GetUtcNow());
        await store.SaveAsync(session, cancellationToken);

        await graph.ResumeInvokeAsync(
            threadId,
            approved ? Command.Approve(ConfirmNode.ApprovePayload) : Command.Reject(reason ?? string.Empty),
            cancellationToken);

        return await journalist.JournalAsync(session, clearPlan: true, cancellationToken);
    }
}

/// <summary>Seed writes of one user turn.</summary>
file static class ChatTurnSeed
{
    public static IReadOnlyList<ChannelWrite> For(ChatSession session, string message)
    {
        return
        [
            new ChannelWrite(ChatChannels.UserMessage, message),
            new ChannelWrite(ChatChannels.SessionId, session.Id.Value.ToString()),
            new ChannelWrite(ChatChannels.SubjectId, session.SubjectId.ToString()),
            new ChannelWrite(ChatChannels.ProjectId, session.ProjectId?.Value.ToString() ?? string.Empty),
        ];
    }
}
