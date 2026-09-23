namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>
/// A fixture target repo under <c>tests/fixtures/target-repos/</c>, cloned
/// through the <c>SourceGitUrl</c>/<c>SourceGitRef</c> mechanism per D4 —
/// see <c>Comuki.EndToEnd.AgentLoop</c>'s README note: that clone-into-
/// container seam is not landed yet (harden-pi-worker-sandbox task 4.1/4.3),
/// so a T2a run does not depend on this being mounted into the container.
/// </summary>
public sealed record ScenarioTargetRepo
{
    /// <summary>Directory name under <c>tests/fixtures/target-repos/</c>.</summary>
    public string Fixture { get; init; } = string.Empty;

    /// <summary>Git ref to check out.</summary>
    public string Ref { get; init; } = "main";
}
