namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>One expected changed file in a diff assertion.</summary>
public sealed record DiffFileAssertion
{
    /// <summary>Path relative to the fixture repo root.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Substring the file's new content must contain; null = unchecked.</summary>
    public string? MustContain { get; init; }
}
