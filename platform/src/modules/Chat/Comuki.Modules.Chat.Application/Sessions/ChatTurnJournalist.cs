using Comuki.Modules.Chat.Application.Graph.Channels;
using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Domain.Ids;
using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Modules.Chat.Domain.Sessions;
using Voluta.Abstractions.Streaming;

namespace Comuki.Modules.Chat.Application.Sessions;

/// <summary>
/// Reads the terminal event of a finished (or interrupted) turn and
/// journals what it produced: digest system entry, tool observation,
/// assistant reply (with the pending approve card). The event's values-mode
/// snapshot is the single source — the checkpoint is never re-read after
/// the run.
/// </summary>
/// <param name="store">Transcript persistence.</param>
/// <param name="clock">Time source for journal stamps.</param>
public sealed class ChatTurnJournalist(
    IChatSessionStore store,
    TimeProvider clock)
{
    /// <summary>Journals the terminal event's state and builds the turn result.</summary>
    /// <param name="session">Session the turn ran on.</param>
    /// <param name="terminal">Terminal event of the turn (values mode).</param>
    /// <param name="cancellationToken"></param>
    public async Task<ChatTurnResult> JournalAsync(
        ChatSession session,
        StreamEvent terminal,
        CancellationToken cancellationToken = default)
    {
        var values = terminal.State ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        var appended = new List<ChatMessage>();

        foreach (var message in ChatTurnJournalRows.Of(session.Id, values, clock.GetUtcNow()))
        {
            await store.AppendAsync(message, cancellationToken);
            appended.Add(message);
        }

        var awaiting = terminal.Kind == StreamEventKind.Interrupt;
        return new ChatTurnResult(
            appended,
            awaiting,
            awaiting ? values.GetValueOrDefault(ChatChannels.PlanJson) as string : null);
    }
}

/// <summary>Terminal values → journal rows.</summary>
file static class ChatTurnJournalRows
{
    public static IEnumerable<ChatMessage> Of(
        ChatSessionId sessionId,
        IReadOnlyDictionary<string, object?> values,
        DateTimeOffset now)
    {
        if (values.GetValueOrDefault(ChatChannels.Digest) is string { Length: > 0 } digest)
        {
            yield return ChatMessage.Create(
                sessionId, ChatMessageRole.System, "memory digest fed to the brain:\n" + digest, toolName: null, now);
        }

        if (values.GetValueOrDefault(ChatChannels.ToolName) is string { Length: > 0 } toolName
            && values.GetValueOrDefault(ChatChannels.ToolResult) is string { Length: > 0 } toolResult)
        {
            yield return ChatMessage.Create(sessionId, ChatMessageRole.Tool, toolResult, toolName, now);
        }

        if (values.GetValueOrDefault(ChatChannels.Reply) is string { Length: > 0 } reply)
        {
            var card = values.GetValueOrDefault(ChatChannels.PlanJson) as string;
            var content = card is { Length: > 0 }
                ? reply + "\n\n" + card
                : reply;
            yield return ChatMessage.Create(sessionId, ChatMessageRole.Assistant, content, toolName: null, now);
        }
    }
}
