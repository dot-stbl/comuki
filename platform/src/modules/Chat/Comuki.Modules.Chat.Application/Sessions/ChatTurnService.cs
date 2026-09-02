using Comuki.Modules.Chat.Application.Graph.Channels;
using Comuki.Modules.Chat.Application.Graph.Confirm;
using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Modules.Chat.Domain.Sessions;
using Voluta.Abstractions.Channels;
using Voluta.Abstractions.Checkpoint;
using Voluta.Abstractions.Runtime;
using Voluta.Abstractions.Streaming;
using Voluta.Graph;

namespace Comuki.Modules.Chat.Application.Sessions;

/// <summary>
/// Drives chat turns over the compiled graph: seeds the turn channels,
/// invokes (or resumes) the graph in values mode and hands the terminal
/// event to the journalist. The transcript is the audit journal — the
/// graph checkpoint carries only routing state.
/// </summary>
/// <param name="store">Transcript + session persistence.</param>
/// <param name="graph">Compiled chat graph (one thread per session).</param>
/// <param name="journalist">Terminal event → transcript journaling.</param>
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

        // Voluta invokes start from an empty channel store — carry-over
        // channels (wizard state) must be re-seeded from the checkpoint so
        // multi-turn flows survive turn boundaries.
        var terminal = await graph.InvokeAsync(
            ChatTurnSeed.For(session, message, ChatTurnCarry.From(state)),
            new RunOptions { ThreadId = threadId, StreamMode = StreamMode.Values },
            cancellationToken);

        return await journalist.JournalAsync(session, terminal, cancellationToken);
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

        // values mode: the terminal event carries the final channel snapshot,
        // which the journalist reads — the checkpoint is never re-read after
        // the run (invoke step numbering restarts per turn, so the newest
        // checkpoint row is not necessarily this turn's terminal state)
        var terminal = await ChatTerminal.DrainAsync(
            graph.ResumeAsync(
                threadId,
                approved ? Command.Approve(ConfirmNode.ApprovePayload) : Command.Reject(reason ?? string.Empty),
                StreamMode.Values,
                cancellationToken),
            cancellationToken);

        return await journalist.JournalAsync(session, terminal, cancellationToken);
    }
}

/// <summary>Seed writes of one user turn.</summary>
file static class ChatTurnSeed
{
    public static IReadOnlyList<ChannelWrite> For(ChatSession session, string message, IReadOnlyList<ChannelWrite> carry)
    {
        return
        [
            new ChannelWrite(ChatChannels.UserMessage, message),
            new ChannelWrite(ChatChannels.SessionId, session.Id.Value.ToString()),
            new ChannelWrite(ChatChannels.SubjectId, session.SubjectId.ToString()),
            new ChannelWrite(ChatChannels.ProjectId, session.ProjectId?.Value.ToString() ?? string.Empty),
            .. carry,
        ];
    }
}

/// <summary>
/// Cross-turn channel carry: Voluta starts every invoke from an empty
/// channel store, so long-lived per-thread state (the /init wizard) must be
/// re-seeded from the latest checkpoint into the next turn's input.
/// </summary>
file static class ChatTurnCarry
{
    private static readonly string[] carryChannels =
    [
        ChatChannels.Wizard,
        ChatChannels.InitStep,
        ChatChannels.InitAnswersJson,
    ];

    public static IReadOnlyList<ChannelWrite> From(ThreadSnapshot? state)
    {
        List<ChannelWrite>? carry = null;

        if (state is not null)
        {
            foreach (var channel in carryChannels)
            {
                if (state.Values.GetValueOrDefault(channel) is string { Length: > 0 } value)
                {
                    (carry ??= []).Add(new ChannelWrite(channel, value));
                }
            }
        }

        return carry is null ? [] : carry;
    }
}

/// <summary>Drains a values-mode stream to its terminal event, rethrowing failures.</summary>
file static class ChatTerminal
{
    public static async Task<StreamEvent> DrainAsync(IAsyncEnumerable<StreamEvent> stream, CancellationToken cancellationToken)
    {
        StreamEvent? terminal = null;

        await foreach (var item in stream.WithCancellation(cancellationToken))
        {
            terminal = item;

            if (item.Kind is StreamEventKind.Failed && item.Payload is Exception exception)
            {
                throw exception;
            }
        }

        return terminal ?? new StreamEvent { Kind = StreamEventKind.End };
    }
}
