namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>One journal-condition assertion: a named condition must evaluate to <see cref="Expected"/>.</summary>
public sealed record JournalConditionAssertion
{
    /// <summary>Condition name — see <see cref="Journal.JournalConditionEvaluator"/> for the vocabulary this runner understands.</summary>
    public string Condition { get; init; } = string.Empty;

    /// <summary>Expected truth value.</summary>
    public bool Expected { get; init; } = true;
}
