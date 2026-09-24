using Comuki.TestFakeModel.Scripting.Loading;
using Comuki.TestFakeModel.Scripting.Model;
using Comuki.TestFakeModel.Scripting.Model.Response;

namespace Comuki.TestFakeModel.Scripting.Building;

/// <summary>
/// Fluent in-code alternative to <see cref="FakeScriptLoader"/> (design.md
/// WS4 4.1) — builds a <see cref="FakeScript"/> without a file on disk,
/// the natural fit for a throwaway xUnit test's own scripted exchange. A
/// thin wrapper over <see cref="Content"/> — see code-shape.md §12.
/// </summary>
public sealed class FakeScriptBuilder
{
    /// <summary>The builder's mutable state — kept off the public surface, per code-shape.md §12's content-struct pattern.</summary>
    internal FakeScriptBuilderContent Content { get; } = new();

    /// <summary>Sets the scenario name deterministic ids are derived from.</summary>
    public FakeScriptBuilder WithScenarioName(string name)
    {
        Content.ScenarioName = name;
        return this;
    }

    /// <summary>Appends an entry that replies with a single text block.</summary>
    public FakeScriptBuilder RespondWithText(
        string text,
        FakeUsage? usage = null,
        FakeScriptMatch? match = null,
        int? requestIndex = null)
    {
        Content.Entries.Add(FakeScriptEntryFactory.ForBlocks(requestIndex, match, [new FakeContentBlock.TextBlock(text)], stopReason: null, usage));
        return this;
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
        Content.Entries.Add(FakeScriptEntryFactory.ForBlocks(requestIndex, match, [block], stopReason: null, usage));
        return this;
    }

    /// <summary>Appends an entry with an arbitrary content block sequence — for a turn that mixes text and tool_use.</summary>
    public FakeScriptBuilder RespondWithBlocks(
        IReadOnlyList<FakeContentBlock> blocks,
        string? stopReason = null,
        FakeUsage? usage = null,
        FakeScriptMatch? match = null,
        int? requestIndex = null)
    {
        Content.Entries.Add(FakeScriptEntryFactory.ForBlocks(requestIndex, match, blocks, stopReason, usage));
        return this;
    }

    /// <summary>Builds the immutable <see cref="FakeScript"/>.</summary>
    public FakeScript Build()
    {
        return new FakeScript(Content.ScenarioName, [.. Content.Entries]);
    }
}

/// <summary>The mutable state behind <see cref="FakeScriptBuilder"/>'s fluent API — see code-shape.md §12.</summary>
internal sealed class FakeScriptBuilderContent
{
    /// <summary>The scenario name deterministic ids are derived from.</summary>
    public string ScenarioName { get; set; } = "fake";

    /// <summary>The entries scripted so far, in call order.</summary>
    public List<FakeScriptEntry> Entries { get; set; } = [];
}

/// <summary>Builds one <see cref="FakeScriptEntry"/>, inferring <see cref="FakeScriptResponse.StopReason"/> when the caller didn't pin one.</summary>
file static class FakeScriptEntryFactory
{
    public static FakeScriptEntry ForBlocks(
        int? requestIndex,
        FakeScriptMatch? match,
        IReadOnlyList<FakeContentBlock> content,
        string? stopReason,
        FakeUsage? usage)
    {
        return new FakeScriptEntry(
            requestIndex,
            match,
            new FakeScriptResponse(
                stopReason ?? (content.Any(static block => block is FakeContentBlock.ToolUseBlock) ? "tool_use" : "end_turn"),
                content,
                usage));
    }
}
