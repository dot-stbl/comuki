using Comuki.Modules.Chat.Application.Graph;
using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Domain.Ids;
using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Modules.Chat.Domain.Sessions;
using Voluta.Abstractions.Channels;
using Voluta.Abstractions.Runtime;
using Voluta.Graph;

namespace Comuki.Modules.Chat.Application.Sessions;

/// <summary>
/// Reads the finished (or interrupted) graph snapshot and journals what the
/// turn produced: digest system entry, tool observation, assistant reply
/// (with the pending approve card), then retires the consumed channels so a
/// later turn never re-journals stale values.
/// </summary>
/// <param name="store">Transcript persistence.</param>
/// <param name="graph">Compiled chat graph (one thread per session).</param>
/// <param name="clock">Time source for journal stamps.</param>
public sealed class ChatTurnJournalist(
    IChatSessionStore store,
    CompiledGraph graph,
    TimeProvider clock)
{
    /// <summary>Journals the current snapshot state and builds the turn result.</summary>
    /// <param name="session">Session the turn ran on.</param>
    /// <param name="clearPlan">Also retire the plan channel (approve resolved).</param>
    /// <param name="cancellationToken"></param>
    public async Task<ChatTurnResult> JournalAsync(ChatSession session, bool clearPlan, CancellationToken cancellationToken = default)
    {
        var threadId = session.Id.Value.ToString();
        var state = await graph.GetStateAsync(threadId, cancellationToken);
        var values = state?.Values ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        var appended = new List<ChatMessage>();

        foreach (var message in ChatTurnJournalRows.Of(session.Id, values, clock.GetUtcNow()))
        {
            await store.AppendAsync(message, cancellationToken);
            appended.Add(message);
        }

        await graph.UpdateStateAsync(threadId, ChatTurnJournalRows.Clears(clearPlan), cancellationToken);

        var awaiting = state?.Status == GraphRunStatus.Interrupted;
        return new ChatTurnResult(
            appended,
            awaiting,
            awaiting ? values.GetValueOrDefault(ChatChannels.PlanJson) as string : null);
    }
}

/// <summary>Snapshot channels → journal rows, and the clears that retire them.</summary>
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

    public static IReadOnlyList<ChannelWrite> Clears(bool clearPlan)
    {
        var clears = new List<ChannelWrite>
        {
            new(ChatChannels.Digest, null),
            new(ChatChannels.ToolName, null),
            new(ChatChannels.ToolResult, null),
            new(ChatChannels.Reply, null),
        };

        if (clearPlan)
        {
            clears.Add(new ChannelWrite(ChatChannels.PlanJson, null));
        }

        return clears;
    }
}
