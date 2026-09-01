namespace Comuki.Modules.Chat.Application.Ports;

/// <summary>
/// Port to the host-side tool runtime: everything the chat graph may do to
/// the outside world beyond the brain. Implemented in the host composition
/// root — the only place allowed to reach orchestration. Unknown tool names
/// fail with <c>chat.tool_unknown</c>.
/// </summary>
public interface IChatToolExecutor
{
    /// <summary>Executes one tool call; failures come back as data.</summary>
    /// <param name="call">Tool name + JSON arguments.</param>
    /// <param name="cancellationToken"></param>
    public Task<ChatToolResult> ExecuteAsync(ChatToolCall call, CancellationToken cancellationToken = default);
}
