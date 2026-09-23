namespace Comuki.TestFakeModel.Scripting;

/// <summary>Maps the on-disk <see cref="FakeScriptFileDto"/> tree onto the <see cref="FakeScript"/> domain model.</summary>
internal static class FakeScriptMapper
{
    /// <summary>Builds a <see cref="FakeScript"/> named <paramref name="scenarioName"/> from <paramref name="dto"/>.</summary>
    public static FakeScript ToDomain(string scenarioName, FakeScriptFileDto dto)
    {
        var entries = new List<FakeScriptEntry>(dto.Entries.Count);
        foreach (var entryDto in dto.Entries)
        {
            entries.Add(ToDomain(entryDto));
        }

        return new FakeScript(scenarioName, entries);
    }

    private static FakeScriptEntry ToDomain(FakeScriptEntryDto dto)
    {
        var match = dto.Match is null
            ? null
            : new FakeScriptMatch(dto.Match.LastUserMessageContains, dto.Match.HasToolResult);

        return new FakeScriptEntry(dto.RequestIndex, match, ToDomain(dto.Response));
    }

    private static FakeScriptResponse ToDomain(FakeScriptResponseDto dto)
    {
        var content = new List<FakeContentBlock>(dto.Content.Count);
        foreach (var blockDto in dto.Content)
        {
            content.Add(ToDomain(blockDto));
        }

        var stopReason = dto.StopReason
            ?? (content.Any(static block => block is FakeContentBlock.ToolUseBlock) ? "tool_use" : "end_turn");

        var usage = dto.Usage is null
            ? null
            : new FakeUsage(dto.Usage.InputTokens, dto.Usage.OutputTokens);

        return new FakeScriptResponse(stopReason, content, usage);
    }

    private static FakeContentBlock ToDomain(FakeContentBlockDto dto)
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
