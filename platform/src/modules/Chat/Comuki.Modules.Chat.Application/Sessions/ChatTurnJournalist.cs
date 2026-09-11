using System.Globalization;
using Comuki.Modules.Chat.Application.Graph.Channels;
using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Domain.Ids;
using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Modules.Chat.Domain.Sessions;
using Comuki.Shared.Contracts.Chat;
using Comuki.Shared.Contracts.Plans;
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

/// <summary>Terminal values → journal rows, each one a list of parts.</summary>
file static class ChatTurnJournalRows
{
    public static IEnumerable<ChatMessage> Of(
        ChatSessionId sessionId,
        IReadOnlyDictionary<string, object?> values,
        DateTimeOffset now)
    {
        if (values.GetValueOrDefault(ChatChannels.Digest) is string { Length: > 0 } digest)
        {
            yield return ChatTranscriptRow.Of(
                sessionId,
                ChatMessageRole.System,
                [new MessagePart.TextPart("memory digest fed to the brain:\n" + digest)],
                now);
        }

        if (values.GetValueOrDefault(ChatChannels.ToolName) is string { Length: > 0 } toolName
            && values.GetValueOrDefault(ChatChannels.ToolResult) is string { Length: > 0 } toolResult)
        {
            yield return ChatTranscriptRow.Of(
                sessionId,
                ChatMessageRole.Tool,
                [ChatToolPart.Of(toolName, toolResult, values)],
                now,
                toolName);
        }

        if (values.GetValueOrDefault(ChatChannels.Reply) is string { Length: > 0 } reply)
        {
            yield return ChatTranscriptRow.Of(
                sessionId,
                ChatMessageRole.Assistant,
                ChatReplyParts.Of(reply, values),
                now);
        }
    }
}

/// <summary>The tool channels of one turn → one tool part.</summary>
file static class ChatToolPart
{
    public static MessagePart.ToolPart Of(
        string toolName,
        string toolResult,
        IReadOnlyDictionary<string, object?> values)
    {
        var status = values.GetValueOrDefault(ChatChannels.ToolStatus) as string;
        return new MessagePart.ToolPart(
            toolName,
            values.GetValueOrDefault(ChatChannels.ToolInput) as string ?? "{}",
            status is { Length: > 0 } && ToolPartStatuses.Parse(status) is { } known ? known : ToolPartStatuses.Success,
            toolResult,
            ChatToolDuration.Of(values.GetValueOrDefault(ChatChannels.ToolDurationMs) as string));
    }
}

/// <summary>Wire-safe duration channel (a string) → milliseconds.</summary>
file static class ChatToolDuration
{
    public static long? Of(string? value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds)
            ? milliseconds
            : null;
    }
}

/// <summary>
/// The assistant row of one turn: the brain's reasoning, its prose and —
/// when the turn produced one — the plan card, as three parts of ONE
/// message instead of a string concatenation.
/// </summary>
file static class ChatReplyParts
{
    public static IReadOnlyList<MessagePart> Of(string reply, IReadOnlyDictionary<string, object?> values)
    {
        List<MessagePart> parts = [];

        if (values.GetValueOrDefault(ChatChannels.Thinking) is string { Length: > 0 } thinking)
        {
            parts.Add(new MessagePart.ThinkingPart(thinking));
        }

        parts.Add(new MessagePart.TextPart(reply));

        if (values.GetValueOrDefault(ChatChannels.PlanJson) is string { Length: > 0 } planJson
            && PlanJson.TryParse(planJson, out var plan, out _))
        {
            parts.Add(new MessagePart.PlanPart(plan.Nodes, plan.Edges));
        }

        return parts;
    }
}
