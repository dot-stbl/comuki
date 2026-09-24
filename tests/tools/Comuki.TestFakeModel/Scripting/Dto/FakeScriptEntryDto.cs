using Comuki.TestFakeModel.Scripting.Dto.Response;

namespace Comuki.TestFakeModel.Scripting.Dto;

/// <summary>On-disk shape of one <c>FakeScriptEntry</c>.</summary>
public sealed record FakeScriptEntryDto
{
    /// <summary>See <c>FakeScriptEntry.RequestIndex</c>.</summary>
    public int? RequestIndex { get; set; }

    /// <summary>See <c>FakeScriptEntry.Match</c>.</summary>
    public FakeScriptMatchDto? Match { get; set; }

    /// <summary>See <c>FakeScriptEntry.Response</c>.</summary>
    public FakeScriptResponseDto Response { get; set; } = new();
}
