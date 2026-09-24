namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>The assertion blocks a scenario run is checked against.</summary>
public sealed record ScenarioAssertions
{
    /// <summary>Journal-condition assertions (see <see cref="Journal.JournalConditionEvaluator"/> for the known vocabulary).</summary>
    public List<JournalConditionAssertion> Journal { get; init; } = [];

    /// <summary>Final work-item/run status assertion.</summary>
    public RunAssertion? Run { get; init; }

    /// <summary>Diff assertions against the fixture repo — T2b/WS7+ (T2a runs TestFakePi, which never edits the workspace).</summary>
    public DiffAssertion? Diff { get; init; }

    /// <summary>Cost ceiling assertions — replay/live only (WS8/WS9); ignored in fake/T2a.</summary>
    public CostAssertion? Cost { get; init; }

    /// <summary>LLM-as-judge rubric assertion — T4/WS10 only; ignored everywhere else.</summary>
    public JudgeAssertion? Judge { get; init; }
}
