using Comuki.Modules.Chat.Application.Slash;
using Voluta.Abstractions.Channels;
using Voluta.Abstractions.Results;
using Voluta.Graph;

namespace Comuki.Modules.Chat.Application.Graph;

/// <summary>
/// Router node — the deterministic v1 turn dispatcher. Slash commands expand
/// to their body and go to the brain; an active wizard keeps consuming
/// answers; a task-looking message without project scope is clarified; the
/// rest goes to think (plan mode for tasks, chat mode otherwise).
/// </summary>
/// <param name="expander">Slash-command expansion over the merged catalog.</param>
public sealed class RouteNode(ChatSlashExpander expander) : IGraphNode
{
    /// <inheritdoc />
    public async Task<NodeResult> InvokeAsync(GraphContext context, CancellationToken cancellationToken = default)
    {
        var message = context.Read<string>(ChatChannels.UserMessage) ?? string.Empty;
        var projectId = context.Read<string>(ChatChannels.ProjectId) ?? string.Empty;
        var wizard = context.Read<string>(ChatChannels.Wizard);

        if (message.StartsWith("/", StringComparison.Ordinal))
        {
            return await expander.ExpandAsync(message, cancellationToken);
        }

        if (wizard == ChatSlashBuiltins.InitKey)
        {
            return NodeResult.Continue(new ChannelWrite(ChatChannels.Phase, ChatPhases.Init));
        }

        var decision = RouteDecision.Of(message, projectId.Length > 0);
        return NodeResult.Continue(
            new ChannelWrite(ChatChannels.Phase, decision.Phase),
            new ChannelWrite(ChatChannels.Task, message),
            new ChannelWrite(ChatChannels.BrainKind, decision.BrainKind));
    }
}

/// <summary>Pure routing decision for non-slash messages.</summary>
/// <param name="Phase">Target phase.</param>
/// <param name="BrainKind">Brain invocation mode.</param>
internal sealed record RouteDecision(string Phase, string BrainKind)
{
    /// <summary>Task-looking messages without project scope are clarified; the rest thinks.</summary>
    /// <param name="message">Raw user message.</param>
    /// <param name="hasProject">Whether the session carries a project scope.</param>
    public static RouteDecision Of(string message, bool hasProject)
    {
        var looksLikeTask = ChatIntent.LooksLikeTask(message);
        return looksLikeTask && !hasProject
            ? new RouteDecision(ChatPhases.Clarify, "chat")
            : new RouteDecision(ChatPhases.Think, looksLikeTask ? "plan" : "chat");
    }
}
