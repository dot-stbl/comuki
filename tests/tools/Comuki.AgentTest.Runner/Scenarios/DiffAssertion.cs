namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>Diff assertions against the fixture repo's working tree after the run — reserved for T2b/WS7.</summary>
public sealed record DiffAssertion
{
    /// <summary>Files the diff must have touched, each with an optional required substring.</summary>
    public List<DiffFileAssertion> FilesChanged { get; init; } = [];

    /// <summary>Whether the diff must include a new/changed test file.</summary>
    public bool? TestsAdded { get; init; }
}
