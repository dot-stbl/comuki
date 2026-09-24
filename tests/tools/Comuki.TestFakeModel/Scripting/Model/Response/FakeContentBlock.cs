using System.Text.Json;

namespace Comuki.TestFakeModel.Scripting.Model.Response;

/// <summary>
/// One content block in a scripted response — a closed hierarchy of
/// <see cref="TextBlock"/> and <see cref="ToolUseBlock"/> (the private
/// constructor keeps it closed to this file; consumers pattern-match).
/// </summary>
public abstract record FakeContentBlock
{
    private FakeContentBlock()
    {
    }

    /// <summary>A plain assistant text block.</summary>
    public sealed record TextBlock(string Text) : FakeContentBlock;

    /// <summary>A <c>tool_use</c> block; <paramref name="Input"/> is the tool call's JSON arguments object.</summary>
    public sealed record ToolUseBlock(string Name, JsonElement Input) : FakeContentBlock;
}
