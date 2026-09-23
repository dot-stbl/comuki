namespace Comuki.TestFakeModel.Scripting;

/// <summary>
/// On-disk shape of a fakeScript file — deserialized from JSON
/// (System.Text.Json, PascalCase-to-camelCase via the loader's options) or
/// YAML (YamlDotNet with the camelCase naming convention) into the exact
/// same DTO tree, then mapped onto the <see cref="FakeScript"/> domain
/// model by <see cref="FakeScriptMapper"/>. Every field is loosely typed
/// on purpose (plain settable properties, no required-ness) — both
/// deserializers populate by property name and neither tolerates
/// constructor binding cleanly.
/// </summary>
internal sealed class FakeScriptFileDto
{
    /// <summary>Overrides the id-generation scenario name; falls back to the script file's base name when omitted.</summary>
    public string? ScenarioName { get; set; }

    /// <summary>The ordered scripted entries.</summary>
    public List<FakeScriptEntryDto> Entries { get; set; } = [];
}

/// <summary>On-disk shape of one <see cref="FakeScriptEntry"/>.</summary>
internal sealed class FakeScriptEntryDto
{
    /// <summary>See <see cref="FakeScriptEntry.RequestIndex"/>.</summary>
    public int? RequestIndex { get; set; }

    /// <summary>See <see cref="FakeScriptEntry.Match"/>.</summary>
    public FakeScriptMatchDto? Match { get; set; }

    /// <summary>See <see cref="FakeScriptEntry.Response"/>.</summary>
    public FakeScriptResponseDto Response { get; set; } = new();
}

/// <summary>On-disk shape of a <see cref="FakeScriptMatch"/>.</summary>
internal sealed class FakeScriptMatchDto
{
    /// <summary>See <see cref="FakeScriptMatch.LastUserMessageContains"/>.</summary>
    public string? LastUserMessageContains { get; set; }

    /// <summary>See <see cref="FakeScriptMatch.HasToolResult"/>.</summary>
    public bool? HasToolResult { get; set; }
}

/// <summary>On-disk shape of a <see cref="FakeScriptResponse"/>.</summary>
internal sealed class FakeScriptResponseDto
{
    /// <summary>
    /// See <see cref="FakeScriptResponse.StopReason"/>. Left unset (<c>null</c>),
    /// the mapper infers <c>tool_use</c> when <see cref="Content"/> has a
    /// <c>tool_use</c> block, else <c>end_turn</c>.
    /// </summary>
    public string? StopReason { get; set; }

    /// <summary>The response's content blocks, in order.</summary>
    public List<FakeContentBlockDto> Content { get; set; } = [];

    /// <summary>See <see cref="FakeScriptResponse.Usage"/>.</summary>
    public FakeUsageDto? Usage { get; set; }
}

/// <summary>
/// On-disk shape of a <see cref="FakeContentBlock"/>. <see cref="Type"/> is
/// <c>"text"</c> (uses <see cref="Text"/>) or <c>"tool_use"</c> (uses
/// <see cref="Name"/> + <see cref="Input"/>); <see cref="Input"/> is
/// deliberately <c>object?</c> — see <see cref="DynamicJsonConverter"/>.
/// </summary>
internal sealed class FakeContentBlockDto
{
    /// <summary><c>"text"</c> or <c>"tool_use"</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The text block's body (<see cref="Type"/> == <c>"text"</c>).</summary>
    public string? Text { get; set; }

    /// <summary>The tool call's name (<see cref="Type"/> == <c>"tool_use"</c>).</summary>
    public string? Name { get; set; }

    /// <summary>The tool call's JSON arguments (<see cref="Type"/> == <c>"tool_use"</c>).</summary>
    public object? Input { get; set; }
}

/// <summary>On-disk shape of a <see cref="FakeUsage"/>.</summary>
internal sealed class FakeUsageDto
{
    /// <summary>See <see cref="FakeUsage.InputTokens"/>.</summary>
    public int? InputTokens { get; set; }

    /// <summary>See <see cref="FakeUsage.OutputTokens"/>.</summary>
    public int? OutputTokens { get; set; }
}
