using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Domain.Ids;
using Comuki.Shared.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Comuki.Host.Realtime.Broadcasting;

/// <summary>
/// SignalR half of the live chat turn: every progress fragment a running
/// turn produces goes to the session's <c>chat:{id}</c> group as
/// <c>ChatChunk</c>, and the turn's terminal signal as
/// <c>ChatTurnComplete</c>. Best-effort by contract (mirrors
/// <see cref="IRunEventsBroadcaster"/>): a broadcast problem is logged and
/// swallowed — live progress must never fail the turn that produced it. The
/// group's membership is permission-checked at join time (RunsHub), so the
/// fan-out addresses exactly the connections the session owner opened.
/// </summary>
/// <param name="hubContext">Hub context without a live connection.</param>
/// <param name="logger"></param>
public sealed class SignalRChatTurnProgress(
    IHubContext<RunsHub> hubContext,
    ILogger<SignalRChatTurnProgress> logger) : IChatTurnProgress
{
    /// <inheritdoc />
    public Task ChunkAsync(ChatSessionId sessionId, int seq, string text, CancellationToken cancellationToken = default)
    {
        return ChatTurnGroupSend.SendAsync(
            hubContext,
            logger,
            sessionId,
            RealtimeTransportMethods.ChatChunk,
            new ChatChunkView(sessionId.Value, seq, text),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task DoneAsync(ChatSessionId sessionId, ChatTurnDone done, CancellationToken cancellationToken = default)
    {
        return ChatTurnGroupSend.SendAsync(
            hubContext,
            logger,
            sessionId,
            RealtimeTransportMethods.ChatTurnComplete,
            new ChatTurnCompleteView(sessionId.Value, ChatTurnOutcomeWords.Of(done)),
            cancellationToken);
    }
}

/// <summary>
/// One best-effort send to a session's live-turn group. Transport faults are
/// logged and swallowed — a dropped chunk is a lost animation frame, never a
/// failed turn; the journal remains the record.
/// </summary>
file static class ChatTurnGroupSend
{
    public static async Task SendAsync(
        IHubContext<RunsHub> hubContext,
        ILogger logger,
        ChatSessionId sessionId,
        string method,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await hubContext.Clients
                .Group(RealtimeGroups.ChatGroup(sessionId))
                .SendAsync(method, payload, cancellationToken);
        }
        catch (Exception exception) when (exception is OperationCanceledException or HubException or ObjectDisposedException)
        {
            logger.LogWarning(
                exception,
                "Chat turn broadcast failed (Session {SessionId}, Method {Method})",
                sessionId.Value,
                method);
        }
    }
}

/// <summary><see cref="ChatTurnDone"/> → the lowercase wire word.</summary>
file static class ChatTurnOutcomeWords
{
    public static string Of(ChatTurnDone done)
    {
        return done switch
        {
            ChatTurnDone.AwaitingApproval => "awaiting_approval",
            ChatTurnDone.Failed => "failed",
            _ => "replied",
        };
    }
}
