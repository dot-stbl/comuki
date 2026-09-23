using Comuki.TestFakeModel.Determinism;
using Comuki.TestFakeModel.Scripting;

namespace Comuki.TestFakeModel.Anthropic;

/// <summary>Builds the non-streaming <c>POST /v1/messages</c> response body from a resolved fakeScript entry.</summary>
internal static class AnthropicResponseFactory
{
    /// <summary>Builds the full response for a non-streaming request.</summary>
    public static AnthropicMessageResponse BuildNonStreaming(
        string messageId,
        string model,
        string scenarioName,
        int requestIndex,
        FakeScriptResponse scripted)
    {
        var (inputTokens, outputTokens) = ResolveUsage(scripted.Usage);
        var content = new List<AnthropicContentBlock>(scripted.Content.Count);
        for (var index = 0; index < scripted.Content.Count; index++)
        {
            content.Add(ToWireBlock(scripted.Content[index], index, scenarioName, requestIndex));
        }

        return new AnthropicMessageResponse(
            messageId,
            "message",
            "assistant",
            model,
            content,
            scripted.StopReason,
            StopSequence: null,
            new AnthropicUsage(inputTokens, outputTokens));
    }

    /// <summary>Resolves scripted usage against <see cref="UsageDefaults"/> for whichever field is unset.</summary>
    public static (int InputTokens, int OutputTokens) ResolveUsage(FakeUsage? usage)
    {
        return (
            usage?.InputTokens ?? UsageDefaults.DefaultInputTokens,
            usage?.OutputTokens ?? UsageDefaults.DefaultOutputTokens);
    }

    /// <summary>Converts one scripted content block into its full (non-streaming) wire shape.</summary>
    public static AnthropicContentBlock ToWireBlock(FakeContentBlock block, int index, string scenarioName, int requestIndex)
    {
        return block switch
        {
            FakeContentBlock.TextBlock text => new AnthropicContentBlock("text", Text: text.Text),
            FakeContentBlock.ToolUseBlock toolUse => new AnthropicContentBlock(
                "tool_use",
                Id: DeterministicIds.ToolUseId(scenarioName, requestIndex, index),
                Name: toolUse.Name,
                Input: toolUse.Input),
            _ => throw new NotSupportedException($"unsupported fake content block type: {block.GetType()}"),
        };
    }
}
