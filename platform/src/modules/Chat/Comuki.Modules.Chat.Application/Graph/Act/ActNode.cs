using System.Text.Json;
using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Application.Graph.Catalog;
using Comuki.Modules.Chat.Application.Graph.Channels;
using Voluta.Abstractions.Channels;
using Voluta.Abstractions.Results;
using Voluta.Graph;

namespace Comuki.Modules.Chat.Application.Graph.Act;

/// <summary>
/// Act node — applies the approved plan through the host tool runtime
/// (<c>create_ticket</c>) and records the run id. Tool failures become an
/// honest assistant reply, never a failed turn.
/// </summary>
/// <param name="tools">Host-side tool executor.</param>
public sealed class ActNode(IChatToolExecutor tools) : IGraphNode
{
    /// <summary>Tool the act node invokes.</summary>
    public const string ToolName = "create_ticket";

    /// <inheritdoc />
    public async Task<NodeResult> InvokeAsync(GraphContext context, CancellationToken cancellationToken = default)
    {
        var planJson = context.Read<string>(ChatChannels.PlanJson) ?? string.Empty;
        var projectId = context.Read<string>(ChatChannels.ProjectId) ?? string.Empty;
        var call = new ChatToolCall(
            ToolName,
            JsonSerializer.Serialize(new ChatTicketArguments(projectId, planJson), JsonSerializerOptions.Web));
        var result = await tools.ExecuteAsync(call, cancellationToken);

        return NodeResult.Continue(
            new ChannelWrite(ChatChannels.ToolName, ToolName),
            new ChannelWrite(ChatChannels.ToolResult, result.ResultJson),
            new ChannelWrite(ChatChannels.Reply, ChatActText.Of(result)),
            new ChannelWrite(ChatChannels.Phase, ChatPhases.Done));
    }
}

/// <summary>Arguments of the <c>create_ticket</c> tool (camelCase on the wire).</summary>
/// <param name="ProjectId">Session project scope; the tool rejects empty.</param>
/// <param name="PlanJson">Canonical approved plan JSON.</param>
internal sealed record ChatTicketArguments(string ProjectId, string PlanJson);

file static class ChatActText
{
    public static string Of(ChatToolResult result)
    {
        return result.Succeeded
            ? "Plan approved — run queued. " + ChatRunIdReading.Of(result.ResultJson)
            : result.NotImplemented
                ? "Plan approved, but queuing the run is not implemented yet (" + result.FailureCode + "). Nothing was queued."
                : "Plan approved, but queuing the run failed (" + result.FailureCode + "). Nothing was queued.";
    }
}

file static class ChatRunIdReading
{
    public static string Of(string resultJson)
    {
        try
        {
            var runId = JsonSerializer.Deserialize<ChatRunIdPayload>(resultJson, JsonSerializerOptions.Web)?.RunId;
            return runId is { Length: > 0 } ? "Run id: " + runId + "." : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}

/// <summary>Payload of a successful create_ticket result.</summary>
/// <param name="RunId">Queued run id (string form).</param>
internal sealed record ChatRunIdPayload(string RunId);
