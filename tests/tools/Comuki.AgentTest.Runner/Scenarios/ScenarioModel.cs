namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>Model wiring — reserved for T2b (WS7) and beyond; the T2a runner reads only <see cref="Mode"/>, and only for reporting.</summary>
public sealed record ScenarioModel
{
    /// <summary>fake | replay | live.</summary>
    public ScenarioModelMode Mode { get; init; } = ScenarioModelMode.Fake;

    /// <summary>Cassette path (relative to the scenario file) — required when <see cref="Mode"/> is <c>replay</c>.</summary>
    public string? Cassette { get; init; }

    /// <summary>Fake-model script path (relative to the scenario file) — WS7+; unused by T2a.</summary>
    public string? FakeScript { get; init; }
}
