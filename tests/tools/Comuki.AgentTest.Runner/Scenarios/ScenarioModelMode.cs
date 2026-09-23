namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>
/// The three execution modes a scenario's <c>model.mode</c> field selects
/// (design.md "Scenario format"). T2a (this workstream) never reads this
/// value to drive execution — it always runs <c>TestFakePi</c>, which is
/// mode-agnostic — but the loader still parses and validates it so the
/// field round-trips for T2b/WS7 (<see cref="Fake"/>/<see cref="Replay"/>
/// wiring a real <c>pi</c> at the fake-model server) and WS8/WS9
/// (<see cref="Replay"/>/<see cref="Live"/>).
/// </summary>
public enum ScenarioModelMode
{
    /// <summary>A scripted fake model — deterministic, no network (WS4/WS7).</summary>
    Fake,

    /// <summary>A recorded cassette served byte-for-byte (WS5/WS8).</summary>
    Replay,

    /// <summary>A real model reached through the hapy gateway, budget-capped (WS9).</summary>
    Live,
}
