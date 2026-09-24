namespace Comuki.TestFakeModel.Scripting.Dto.Response;

/// <summary>On-disk shape of a <c>FakeScriptResponse</c>.</summary>
public sealed record FakeScriptResponseDto
{
    /// <summary>
    /// See <c>FakeScriptResponse.StopReason</c>. Left unset (<c>null</c>),
    /// the mapper infers <c>tool_use</c> when <see cref="Content"/> has a
    /// <c>tool_use</c> block, else <c>end_turn</c>.
    /// </summary>
    public string? StopReason { get; set; }

    /// <summary>The response's content blocks, in order.</summary>
    public List<FakeContentBlockDto> Content { get; set; } = [];

    /// <summary>See <c>FakeScriptResponse.Usage</c>.</summary>
    public FakeUsageDto? Usage { get; set; }
}
