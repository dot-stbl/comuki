using Comuki.Host.Translator.Parsing;
using Comuki.Shared.Contracts.Grpc;

namespace Comuki.Host.Translator.Runtime;

/// <summary>
/// Pure mapping of typed pi events to forwardable
/// <see cref="WorkerEvent"/> activities. Session headers, the final result
/// and events we don't model are not forwarded (null) — the loop builds
/// the Start/Report envelope itself from the claim and the run outcome.
/// </summary>
public static class PiEventToWorkerEvent
{
    /// <summary>Maps one pi event to a forwardable Activity event; null for non-forwardable events.</summary>
    /// <param name="workItemId"></param>
    /// <param name="piEvent"></param>
    public static WorkerEvent? ToForwardEvent(string workItemId, PiEvent piEvent)
    {
        return piEvent switch
        {
            PiEvent.TextDeltaEvent delta => new WorkerEvent
            {
                Activity = new StageActivity { WorkItemId = workItemId, Text = delta.Delta },
            },
            PiEvent.AssistantTextEvent text => new WorkerEvent
            {
                Activity = new StageActivity { WorkItemId = workItemId, Text = text.Text },
            },
            PiEvent.AssistantToolUseEvent tool => new WorkerEvent
            {
                Activity = new StageActivity
                {
                    WorkItemId = workItemId,
                    Tool = tool.Tool,
                    ToolInputJson = tool.InputJson,
                },
            },
            PiEvent.ToolCallEvent toolCall => new WorkerEvent
            {
                Activity = new StageActivity
                {
                    WorkItemId = workItemId,
                    Tool = toolCall.ToolName,
                    ToolInputJson = toolCall.ArgsJson,
                },
            },
            _ => null,
        };
    }
}
