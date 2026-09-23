namespace Comuki.TestFakeModel.Scripting;

/// <summary>
/// Fluent in-code alternative to <see cref="FakeScriptLoader"/> (design.md
/// WS4 4.1) — builds a <see cref="FakeScript"/> without a file on disk,
/// the natural fit for a throwaway xUnit test's own scripted exchange.
/// </summary>
public sealed class FakeScriptBuilder
{
    private readonly List<FakeScriptEntry> entries = [];
    private string scenarioName = "fake";

    /// <summary>Sets the scenario name deterministic ids are derived from.</summary>
    public FakeScriptBuilder WithScenarioName(string name)
    {
        scenarioName = name;
        return this;
    }

    /// <summary>Appends an entry that replies with a single text block.</summary>
    public FakeScriptBuilder RespondWithText(
        string text,
        FakeUsage? usage = null,
        FakeScriptMatch? match = null,
        int? requestIndex = null)
    {
        return AddEntry(requestIndex, match, [new FakeContentBlock.TextBlock(text)], usage, stopReason: null);
    }

    /// <summary>Appends an entry that replies with a single <c>tool_use</c> block.</summary>
    public FakeScriptBuilder RespondWithToolUse(
        string toolName,
        object? input = null,
        FakeUsage? usage = null,
        FakeScriptMatch? match = null,
        int? requestIndex = null)
    {
        var block = new FakeContentBlock.ToolUseBlock(toolName, DynamicJsonConverter.ToJsonElement(input));
        return AddEntry(requestIndex, match, [block], usage, stopReason: null);
    }

    /// <summary>Appends an entry with an arbitrary content block sequence — for a turn that mixes text and tool_use.</summary>
    public FakeScriptBuilder RespondWithBlocks(
        IReadOnlyList<FakeContentBlock> blocks,
        string? stopReason = null,
        FakeUsage? usage = null,
        FakeScriptMatch? match = null,
        int? requestIndex = null)
    {
        return AddEntry(requestIndex, match, blocks, usage, stopReason);
    }

    /// <summary>Builds the immutable <see cref="FakeScript"/>.</summary>
    public FakeScript Build()
    {
        return new FakeScript(scenarioName, [.. entries]);
    }

    private FakeScriptBuilder AddEntry(
        int? requestIndex,
        FakeScriptMatch? match,
        IReadOnlyList<FakeContentBlock> content,
        FakeUsage? usage,
        string? stopReason)
    {
        var resolvedStopReason = stopReason
            ?? (content.Any(static block => block is FakeContentBlock.ToolUseBlock) ? "tool_use" : "end_turn");

        entries.Add(new FakeScriptEntry(requestIndex, match, new FakeScriptResponse(resolvedStopReason, content, usage)));
        return this;
    }
}
