using Comuki.TestFakeModel.Anthropic.Wire;
using Comuki.TestFakeModel.Anthropic.Wire.Blocks;
using Comuki.TestFakeModel.Determinism;
using Comuki.TestFakeModel.Scripting.Model.Response;

namespace Comuki.TestFakeModel.Anthropic.Response;

/// <summary>Builds the non-streaming <c>POST /v1/messages</c> response body from a resolved fakeScript entry.</summary>
public static class AnthropicResponseFactory
{
    /// <summary>Builds the full response for a non-streaming request.</summary>
    public static AnthropicMessageResponse BuildNonStreaming(
        string messageId,
        string model,
        string scenarioName,
        int requestIndex,
        FakeScriptResponse scripted)
    {
        return new AnthropicMessageResponse(
            messageId,
            "message",
            "assistant",
            model,
            [.. scripted.Content.Select((block, index) => ToWireBlock(block, index, scenarioName, requestIndex))],
            scripted.StopReason,
            StopSequence: null,
            ResolveUsage(scripted.Usage));
    }

    /// <summary>Resolves scripted usage against <see cref="UsageDefaults"/> for whichever field is unset.</summary>
    public static AnthropicUsage ResolveUsage(FakeUsage? usage)
    {
        return new AnthropicUsage(
            usage?.InputTokens ?? UsageDefaults.DefaultInputTokens,
            usage?.OutputTokens ?? UsageDefaults.DefaultOutputTokens);
    }

    /// <summary>Converts one scripted content block into its full (non-streaming) wire shape — an <see cref="AnthropicTextBlock"/> or <see cref="AnthropicToolUseBlock"/>.</summary>
    public static object ToWireBlock(FakeContentBlock block, int index, string scenarioName, int requestIndex)
    {
        return block switch
        {
            FakeContentBlock.TextBlock text => new AnthropicTextBlock("text", text.Text),
            FakeContentBlock.ToolUseBlock toolUse => new AnthropicToolUseBlock(
                "tool_use",
                DeterministicIds.ToolUseId(scenarioName, requestIndex, index),
                toolUse.Name,
                toolUse.Input),
            _ => throw new NotSupportedException($"unsupported fake content block type: {block.GetType()}"),
        };
    }
}
