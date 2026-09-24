namespace Comuki.TestFakeModel.Scripting.Dto.Response;

/// <summary>
/// On-disk shape of a <c>FakeContentBlock</c>. <see cref="Type"/> is
/// <c>"text"</c> (uses <see cref="Text"/>) or <c>"tool_use"</c> (uses
/// <see cref="Name"/> + <see cref="Input"/>); <see cref="Input"/> is
/// deliberately <c>object?</c> — see <c>DynamicJsonConverter</c>.
/// </summary>
public sealed record FakeContentBlockDto
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
