namespace Comuki.TestFakeModel.Scripting.Dto.Response;

/// <summary>On-disk shape of a <c>FakeUsage</c>.</summary>
public sealed record FakeUsageDto
{
    /// <summary>See <c>FakeUsage.InputTokens</c>.</summary>
    public int? InputTokens { get; set; }

    /// <summary>See <c>FakeUsage.OutputTokens</c>.</summary>
    public int? OutputTokens { get; set; }
}
