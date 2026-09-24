namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>
/// A scenario file failed to load or violates a structural rule (D-format
/// requirements from add-agentic-test-contour's spec, e.g. "replay mode
/// declared with no cassette on disk"). The runner surfaces this as a
/// report failure naming the scenario and the exact problem — never a bare
/// stack trace.
/// </summary>
/// <param name="message">Message describing the violated rule.</param>
/// <param name="innerException">The wrapped parse failure, if any.</param>
public sealed class ScenarioValidationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
