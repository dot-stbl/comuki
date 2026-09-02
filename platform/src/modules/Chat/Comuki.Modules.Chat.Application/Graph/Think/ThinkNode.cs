using System.Text.Json;
using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Application.Graph.Catalog;
using Comuki.Modules.Chat.Application.Graph.Channels;
using Comuki.Modules.Chat.Application.Graph.Confirm;
using Comuki.Modules.Chat.Domain.Ids;
using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Shared.Contracts.Brain;
using Comuki.Shared.Contracts.Memory;
using Voluta.Abstractions.Channels;
using Voluta.Abstractions.Results;
using Voluta.Graph;

namespace Comuki.Modules.Chat.Application.Graph.Think;

/// <summary>
/// Think node — assembles the brain context (memory digest + history tail),
/// invokes the brain once, and gates plan output through
/// <see cref="ChatPlanGate"/>. The digest it fed the brain lands on the
/// digest channel so the turn service journals it as a system message
/// (audit per the memory contract).
/// </summary>
/// <param name="brain">Brain port (gRPC client or in-process stub).</param>
/// <param name="memoryDigest">Shared digest service (variant Z).</param>
/// <param name="sessions">Transcript reads for the history window.</param>
public sealed class ThinkNode(
    IBrainClient brain,
    IMemoryDigest memoryDigest,
    IChatSessionStore sessions) : IGraphNode
{
    /// <summary>How many newest transcript rows ride along in the brain context.</summary>
    public const int HistoryWindow = 20;

    /// <inheritdoc />
    public async Task<NodeResult> InvokeAsync(GraphContext context, CancellationToken cancellationToken = default)
    {
        var task = context.Read<string>(ChatChannels.Task) ?? string.Empty;
        var brainKind = context.Read<string>(ChatChannels.BrainKind) ?? "chat";
        var sessionId = ChatSessionIdParsing.Parse(context.Read<string>(ChatChannels.SessionId));
        var scope = ChatDigestScope.Of(
            context.Read<string>(ChatChannels.SubjectId) ?? string.Empty,
            context.Read<string>(ChatChannels.ProjectId) ?? string.Empty);

        var digest = await memoryDigest.BuildDigestAsync(
            new MemoryDigestRequest(scope.ScopeKind, scope.SubjectId, task),
            cancellationToken);
        var history = await sessions.ReadRecentAsync(sessionId, HistoryWindow, cancellationToken);
        var reply = await brain.InvokeAsync(
            new BrainRequest { Kind = brainKind, ContextJson = ChatBrainContextJson.ToJson(history, digest), Task = task },
            cancellationToken);

        if (brainKind != "plan")
        {
            return NodeResult.Continue(
                new ChannelWrite(ChatChannels.Digest, digest),
                new ChannelWrite(ChatChannels.Reply, reply.FinalJson),
                new ChannelWrite(ChatChannels.Phase, ChatPhases.Done));
        }

        var outcome = ChatPlanGate.Validate(reply.FinalJson);
        return outcome.Plan is null
            ? NodeResult.Continue(
                new ChannelWrite(ChatChannels.Digest, digest),
                new ChannelWrite(ChatChannels.Reply, ChatPlanGate.InvalidPlanMessage),
                new ChannelWrite(ChatChannels.Phase, ChatPhases.Done))
            : NodeResult.Continue(
                new ChannelWrite(ChatChannels.Digest, digest),
                new ChannelWrite(ChatChannels.PlanJson, outcome.CanonicalJson),
                new ChannelWrite(ChatChannels.Reply, ChatPlanGate.CardPrompt),
                new ChannelWrite(ChatChannels.Phase, ChatPhases.Confirm));
    }
}

/// <summary>Subject/project channel pair → digest scope (project scope wins when present).</summary>
/// <param name="ScopeKind"></param>
/// <param name="SubjectId"></param>
internal sealed record ChatDigestScope(string ScopeKind, Guid SubjectId)
{
    /// <summary>Maps channel strings onto the memory scope model.</summary>
    /// <param name="subjectId"></param>
    /// <param name="projectId"></param>
    public static ChatDigestScope Of(string subjectId, string projectId)
    {
        return Guid.TryParse(projectId, out var project)
            ? new ChatDigestScope(MemoryDigestScopes.Project, project)
            : new ChatDigestScope(MemoryDigestScopes.User, Guid.TryParse(subjectId, out var subject) ? subject : Guid.Empty);
    }
}

/// <summary>Brain context JSON shape (camelCase on the wire).</summary>
/// <param name="History">Newest transcript rows, oldest first.</param>
/// <param name="Digest">Memory digest text (may be empty).</param>
internal sealed record ChatBrainContext(IReadOnlyList<ChatBrainHistoryEntry> History, string Digest);

/// <summary>One history row of the brain context.</summary>
/// <param name="Role">Lower-cased role name.</param>
/// <param name="Content">Message text.</param>
internal sealed record ChatBrainHistoryEntry(string Role, string Content);

file static class ChatSessionIdParsing
{
    public static ChatSessionId Parse(string? value)
    {
        return Guid.TryParse(value, out var id) ? new ChatSessionId(id) : ChatSessionId.New();
    }
}

/// <summary>Brain context assembly — history tail + digest, camelCase JSON.</summary>
file static class ChatBrainContextJson
{
    public static string ToJson(IReadOnlyList<ChatMessage> history, string digest)
    {
        var entries = history.Select(
            static message => new ChatBrainHistoryEntry(
                message.Role.ToString().ToLowerInvariant(),
                message.Content));

        return JsonSerializer.Serialize(new ChatBrainContext([.. entries], digest), JsonSerializerOptions.Web);
    }
}
