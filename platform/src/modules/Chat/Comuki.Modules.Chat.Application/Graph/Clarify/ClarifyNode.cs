using Comuki.Modules.Chat.Application.Graph.Catalog;
using Comuki.Modules.Chat.Application.Graph.Channels;
using Voluta.Abstractions.Channels;
using Voluta.Abstractions.Results;
using Voluta.Graph;

namespace Comuki.Modules.Chat.Application.Graph.Clarify;

/// <summary>
/// Clarify node — a task-looking message arrived without project scope, so
/// the turn asks the two questions it needs (which project, what exactly)
/// instead of calling the brain. The next message re-enters the router with
/// the answer.
/// </summary>
public sealed class ClarifyNode : IGraphNode
{
    /// <inheritdoc />
    public Task<NodeResult> InvokeAsync(GraphContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<NodeResult>(NodeResult.Continue(
            new ChannelWrite(ChatChannels.Reply, ClarifyText.Value),
            new ChannelWrite(ChatChannels.Phase, ChatPhases.Done)));
    }
}

file static class ClarifyText
{
    public const string Value =
        "I can plan this, but the session has no project scope yet. Which project should this target "
        + "(start a session with a projectId, or run /init), and what exactly should be done?";
}
