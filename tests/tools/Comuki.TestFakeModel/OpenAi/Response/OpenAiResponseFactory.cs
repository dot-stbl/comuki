using System.Text.Json;
using Comuki.TestFakeModel.Determinism;
using Comuki.TestFakeModel.OpenAi.Wire;
using Comuki.TestFakeModel.OpenAi.Wire.Message;
using Comuki.TestFakeModel.Scripting.Model.Response;

namespace Comuki.TestFakeModel.OpenAi.Response;

/// <summary>Builds the non-streaming <c>POST /v1/chat/completions</c> response body from a resolved fakeScript entry.</summary>
public static class OpenAiResponseFactory
{
    /// <summary>Builds the full response for a non-streaming request.</summary>
    public static OpenAiChatCompletionResponse BuildNonStreaming(
        string chatCompletionId,
        string model,
        long createdUnixSeconds,
        string scenarioName,
        int requestIndex,
        FakeScriptResponse scripted)
    {
        var toolCalls = OpenAiToolCallProjector.ToToolCalls(scripted.Content, scenarioName, requestIndex);
        var message = new OpenAiChatMessage(
            "assistant",
            OpenAiTextProjector.JoinText(scripted.Content),
            toolCalls.Count == 0 ? null : toolCalls);

        return new OpenAiChatCompletionResponse(
            chatCompletionId,
            "chat.completion",
            createdUnixSeconds,
            model,
            [new OpenAiChoice(0, message, OpenAiFinishReasonMapper.Map(scripted.StopReason))],
            ResolveUsage(scripted.Usage));
    }

    /// <summary>Resolves scripted usage against <see cref="UsageDefaults"/> for whichever field is unset.</summary>
    public static OpenAiUsage ResolveUsage(FakeUsage? usage)
    {
        var promptTokens = usage?.InputTokens ?? UsageDefaults.DefaultInputTokens;
        var completionTokens = usage?.OutputTokens ?? UsageDefaults.DefaultOutputTokens;
        return new OpenAiUsage(promptTokens, completionTokens, promptTokens + completionTokens);
    }
}

/// <summary>Joins every scripted text block into the message's single <c>content</c> string — extracted per class-layout-and-tooling.md §1a.</summary>
file static class OpenAiTextProjector
{
    public static string? JoinText(IReadOnlyList<FakeContentBlock> content)
    {
        var texts = content.OfType<FakeContentBlock.TextBlock>().Select(static block => block.Text).ToList();
        return texts.Count == 0 ? null : string.Join('\n', texts);
    }
}

/// <summary>Projects the scripted <c>tool_use</c> blocks onto OpenAI's <c>tool_calls</c> array — extracted per class-layout-and-tooling.md §1a.</summary>
file static class OpenAiToolCallProjector
{
    public static IReadOnlyList<OpenAiToolCall> ToToolCalls(IReadOnlyList<FakeContentBlock> content, string scenarioName, int requestIndex)
    {
        var toolCallIndex = 0;
        var toolCalls = new List<OpenAiToolCall>();
        foreach (var block in content)
        {
            if (block is not FakeContentBlock.ToolUseBlock toolUse)
            {
                continue;
            }

            toolCalls.Add(new OpenAiToolCall(
                DeterministicIds.ToolCallId(scenarioName, requestIndex, toolCallIndex),
                "function",
                new OpenAiToolCallFunction(toolUse.Name, JsonSerializer.Serialize(toolUse.Input, JsonSerializerOptions.Web))));
            toolCallIndex++;
        }

        return toolCalls;
    }
}
