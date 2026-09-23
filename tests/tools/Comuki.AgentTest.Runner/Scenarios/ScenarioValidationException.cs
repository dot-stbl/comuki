namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>
/// A scenario file failed to load or violates a structural rule (D-format
/// requirements from add-agentic-test-contour's spec, e.g. "replay mode
/// declared with no cassette on disk"). The runner surfaces this as a
/// report failure naming the scenario and the exact problem — never a bare
/// stack trace.
/// </summary>
public sealed class ScenarioValidationException : Exception
{
    /// <summary>Creates the exception with a message describing the violated rule.</summary>
    /// <param name="message"></param>
    public ScenarioValidationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception wrapping an inner parse failure.</summary>
    /// <param name="message"></param>
    /// <param name="innerException"></param>
    public ScenarioValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
