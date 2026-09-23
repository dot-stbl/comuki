namespace Comuki.TestFakeModel.Scripting.Dto;

/// <summary>On-disk shape of a <c>FakeScriptMatch</c>.</summary>
public sealed record FakeScriptMatchDto
{
    /// <summary>See <c>FakeScriptMatch.LastUserMessageContains</c>.</summary>
    public string? LastUserMessageContains { get; set; }

    /// <summary>See <c>FakeScriptMatch.HasToolResult</c>.</summary>
    public bool? HasToolResult { get; set; }
}
