namespace Comuki.TestFakeModel.Determinism;

/// <summary>
/// Fixed, reproducible id formats for every id the fake server hands out —
/// design.md's "Determinism knobs": <c>msg_&lt;scenarioName&gt;_&lt;n&gt;</c>
/// for message ids (matching the cassette format's worked example) and an
/// analogous shape for tool_use/tool_call ids, both derived only from the
/// scenario name and the 1-based request/block position — never from
/// randomness or the wall clock. <see cref="MessageId"/>/<see cref="ToolUseId"/>
/// back the Anthropic wire shape; <see cref="ChatCompletionId"/>/
/// <see cref="ToolCallId"/> back the OpenAI wire shape — same derivation,
/// different literal prefix so a fixture's id is recognizable at a glance.
/// </summary>
public static class DeterministicIds
{
    /// <summary>The <c>message_start</c>/non-streaming response id for the <paramref name="requestIndex"/>-th request.</summary>
    public static string MessageId(string scenarioName, int requestIndex)
    {
        return $"msg_{scenarioName}_{requestIndex}";
    }

    /// <summary>The <c>tool_use</c> block id for block <paramref name="blockIndex"/> of the <paramref name="requestIndex"/>-th request's response.</summary>
    public static string ToolUseId(string scenarioName, int requestIndex, int blockIndex)
    {
        return $"toolu_{scenarioName}_{requestIndex}_{blockIndex}";
    }

    /// <summary>The OpenAI <c>chat.completion</c>/<c>chat.completion.chunk</c> id for the <paramref name="requestIndex"/>-th request.</summary>
    public static string ChatCompletionId(string scenarioName, int requestIndex)
    {
        return $"chatcmpl_{scenarioName}_{requestIndex}";
    }

    /// <summary>The OpenAI <c>tool_calls[].id</c> for tool call <paramref name="toolCallIndex"/> of the <paramref name="requestIndex"/>-th request's response.</summary>
    public static string ToolCallId(string scenarioName, int requestIndex, int toolCallIndex)
    {
        return $"call_{scenarioName}_{requestIndex}_{toolCallIndex}";
    }
}
