namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>
/// One declarative scenario (design.md "Scenario format" of
/// add-agentic-test-contour): a ticket fixture, the worker/model wiring,
/// an expected tool-call trajectory and the assertions a
/// <see cref="ScenarioModelMode"/> run must satisfy. Deserialized from YAML
/// by <see cref="ScenarioLoader"/> — property names here are
/// camelCase-mapped onto the YAML keys, not renamed per-field.
/// </summary>
public sealed record ScenarioDefinition
{
    /// <summary>Schema version of this file's shape; the loader rejects anything but <c>1</c>.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Short, unique, kebab-case scenario name — also its report key and container-name suffix.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Free-text description of what the scenario exercises and why.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The inbound ticket this scenario seeds.</summary>
    public ScenarioTicket Ticket { get; init; } = new();

    /// <summary>Claim labels and (T2a-only) the pi fixture stream the worker container replays.</summary>
    public ScenarioWorker Worker { get; init; } = new();

    /// <summary>
    /// Model wiring — reserved for T2b/T4 (WS7/WS9): a real <c>pi</c> pointed
    /// at the fake-model/replay/live server. Null (or a bare <c>mode: fake</c>
    /// with no cassette) is normal for a T2a scenario, which never calls a
    /// model at all — <see cref="ScenarioWorker.PiFixturesDir"/> is what
    /// drives <c>TestFakePi</c> instead.
    /// </summary>
    public ScenarioModel? Model { get; init; }

    /// <summary>Expected tool-call trajectory per stage — asserted against the tools pi/TestFakePi actually invoked.</summary>
    public List<ExpectedTrajectoryStage> ExpectedTrajectory { get; init; } = [];

    /// <summary>The assertions this scenario's run must satisfy.</summary>
    public ScenarioAssertions Assertions { get; init; } = new();

    /// <summary>Live/replay-record budget ceiling — reserved for T4/WS8/WS9; ignored in T2a.</summary>
    public ScenarioBudget? Budget { get; init; }
}
