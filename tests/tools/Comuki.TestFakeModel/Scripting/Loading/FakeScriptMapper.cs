using Comuki.TestFakeModel.Scripting.Dto;
using Comuki.TestFakeModel.Scripting.Dto.Response;
using Comuki.TestFakeModel.Scripting.Model;
using Comuki.TestFakeModel.Scripting.Model.Response;

namespace Comuki.TestFakeModel.Scripting.Loading;

/// <summary>Maps the on-disk <see cref="FakeScriptFileDto"/> tree onto the <see cref="FakeScript"/> domain model.</summary>
public static class FakeScriptMapper
{
    /// <summary>Builds a <see cref="FakeScript"/> named <paramref name="scenarioName"/> from <paramref name="dto"/>.</summary>
    public static FakeScript ToDomain(string scenarioName, FakeScriptFileDto dto)
    {
        return new FakeScript(scenarioName, [.. dto.Entries.Select(FakeScriptEntrySteps.ToFakeScriptEntry)]);
    }
}

/// <summary>The per-level mapping steps <see cref="FakeScriptMapper"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class FakeScriptEntrySteps
{
    public static FakeScriptEntry ToFakeScriptEntry(FakeScriptEntryDto dto)
    {
        return new FakeScriptEntry(
            dto.RequestIndex,
            dto.Match is null ? null : new FakeScriptMatch(dto.Match.LastUserMessageContains, dto.Match.HasToolResult),
            ToFakeScriptResponse(dto.Response));
    }

    public static FakeScriptResponse ToFakeScriptResponse(FakeScriptResponseDto dto)
    {
        var content = dto.Content.Select(ToFakeContentBlock).ToList();
        return new FakeScriptResponse(
            dto.StopReason ?? (content.Any(static block => block is FakeContentBlock.ToolUseBlock) ? "tool_use" : "end_turn"),
            content,
            dto.Usage is null ? null : new FakeUsage(dto.Usage.InputTokens, dto.Usage.OutputTokens));
    }

    public static FakeContentBlock ToFakeContentBlock(FakeContentBlockDto dto)
    {
        return dto.Type switch
        {
            "text" => new FakeContentBlock.TextBlock(dto.Text ?? string.Empty),
            "tool_use" => new FakeContentBlock.ToolUseBlock(
                dto.Name ?? throw new FakeScriptException("fakeScript content block of type 'tool_use' is missing 'name'."),
                DynamicJsonConverter.ToJsonElement(dto.Input)),
            _ => throw new FakeScriptException($"fakeScript content block has unknown type '{dto.Type}' (expected 'text' or 'tool_use')."),
        };
    }
}
