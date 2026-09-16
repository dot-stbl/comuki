using System.Runtime.ExceptionServices;
using Comuki.Modules.Chat.Application.Graph.Channels;
using Comuki.Modules.Chat.Application.Graph.Confirm;
using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Modules.Chat.Domain.Sessions;
using Comuki.Shared.Contracts.Chat;
using Voluta.Abstractions.Channels;
using Voluta.Abstractions.Checkpoint;
using Voluta.Abstractions.Runtime;
using Voluta.Abstractions.Streaming;
using Voluta.Exceptions.Run;
using Voluta.Graph;

namespace Comuki.Modules.Chat.Application.Sessions;

/// <summary>
/// Drives chat turns over the compiled graph: seeds the turn channels,
/// invokes (or resumes) the graph in values mode and hands the terminal
/// event to the journalist. The transcript is the audit journal — the
/// graph checkpoint carries only routing state. While the graph runs, the
/// turn's live progress flows out through <see cref="IChatTurnProgress"/>;
/// every exit path (reply, approve interrupt, failure) emits its terminal
/// signal so a streaming client never dangles.
/// </summary>
/// <param name="store">Transcript + session persistence.</param>
/// <param name="graph">Compiled chat graph (one thread per session).</param>
/// <param name="journalist">Terminal event → transcript journaling.</param>
/// <param name="progress">Live turn progress fan-out.</param>
/// <param name="clock">Time source for journal stamps.</param>
public sealed class ChatTurnService(
    IChatSessionStore store,
    CompiledGraph graph,
    ChatTurnJournalist journalist,
    IChatTurnProgress progress,
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
            ChatTranscriptRow.Of(
                session.Id,
                ChatMessageRole.User,
                [new MessagePart.TextPart(message)],
                clock.GetUtcNow()),
            cancellationToken);
        session.Touch(clock.GetUtcNow());
        await store.SaveAsync(session, cancellationToken);

        // Voluta invokes start from an empty channel store — carry-over
        // channels (wizard state) must be re-seeded from the checkpoint so
        // multi-turn flows survive turn boundaries.
        var terminal = await ChatTurnRun.AwaitAsync(
            progress,
            session,
            ChatGraphRun.InvokeAsync(
                graph,
                ChatTurnSeed.For(session, message, ChatTurnCarry.From(state)),
                new RunOptions { ThreadId = threadId, StreamMode = StreamMode.Values },
                cancellationToken),
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
            ChatTranscriptRow.Of(
                session.Id,
                ChatMessageRole.User,
                [
                    new MessagePart.TextPart(
                        approved ? "approve" : ("reject" + (reason is { Length: > 0 } ? ": " + reason : string.Empty))),
                ],
                clock.GetUtcNow()),
            cancellationToken);
        session.Touch(clock.GetUtcNow());
        await store.SaveAsync(session, cancellationToken);

        // values mode: the terminal event carries the final channel snapshot,
        // which the journalist reads — the checkpoint is never re-read after
        // the run (invoke step numbering restarts per turn, so the newest
        // checkpoint row is not necessarily this turn's terminal state)
        var terminal = await ChatTurnRun.AwaitAsync(
            progress,
            session,
            ChatTerminal.DrainAsync(
                graph.ResumeAsync(
                    threadId,
                    approved ? Command.Approve(ConfirmNode.ApprovePayload) : Command.Reject(reason ?? string.Empty),
                    StreamMode.Values,
                    cancellationToken),
                cancellationToken),
            cancellationToken);

        return await journalist.JournalAsync(session, terminal, cancellationToken);
    }
}

/// <summary>
/// Runs one turn and lets a node's own exception through. Voluta wraps
/// whatever a node threw in <see cref="GraphRunFailedException"/>, which
/// would hide the typed faults the HTTP surface maps (a pending approve,
/// an unreachable brain) behind a generic 500. The resume path already
/// rethrows the node's exception (<see cref="ChatTerminal"/>), so both
/// paths surface the same thing.
/// </summary>
file static class ChatGraphRun
{
    public static async Task<StreamEvent> InvokeAsync(
        CompiledGraph graph,
        IReadOnlyList<ChannelWrite> seed,
        RunOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            return await graph.InvokeAsync(seed, options, cancellationToken);
        }
        catch (GraphRunFailedException exception) when (exception.InnerException is { } inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }
}

/// <summary>
/// Runs one turn's event stream to its terminal event, emitting the terminal
/// progress signal on every exit path: the event's kind decides replied vs
/// awaiting-approval, and a failure emits <see cref="ChatTurnDone.Failed"/>
/// before the typed fault continues to the HTTP surface — a streaming client
/// must not keep its live overlay after the turn it was watching died.
/// </summary>
file static class ChatTurnRun
{
    public static async Task<StreamEvent> AwaitAsync(
        IChatTurnProgress progress,
        ChatSession session,
        Task<StreamEvent> run,
        CancellationToken cancellationToken)
    {
        StreamEvent terminal;

        try
        {
            terminal = await run;
        }
        catch (Exception)
        {
            // boundary: observe-and-notify, then rethrow — the typed fault
            // (pending approve, unreachable brain) still reaches the HTTP
            // ProblemDetails mapper untouched.
            await progress.DoneAsync(session.Id, ChatTurnDone.Failed, cancellationToken);
            throw;
        }

        await progress.DoneAsync(
            session.Id,
            terminal.Kind == StreamEventKind.Interrupt ? ChatTurnDone.AwaitingApproval : ChatTurnDone.Replied,
            cancellationToken);

        return terminal;
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
