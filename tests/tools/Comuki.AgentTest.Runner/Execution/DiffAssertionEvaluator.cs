using Comuki.AgentTest.Runner.Scenarios;

namespace Comuki.AgentTest.Runner.Execution;

/// <summary>
/// Evaluates a scenario's <c>assertions.diff</c> against the real working
/// directory pi ran in (WS7 task 7.3) — content-based, not a real
/// <c>git diff</c>: it reads each declared file's post-run content and
/// checks the required substring. <see cref="IAgentLoopHarness.ResolveWorkingDirectoryAsync"/>
/// returns null for a harness with no host-visible working tree (T2a's
/// container harness) — <see cref="ScenarioRunner"/> skips Diff assertions
/// entirely in that case, matching <see cref="DiffAssertion"/>'s own doc
/// comment ("T2a runs TestFakePi, which never edits the workspace").
/// </summary>
public static class DiffAssertionEvaluator
{
    /// <summary>Returns a failure message, or null when every declared file/testsAdded check holds.</summary>
    public static async Task<string?> EvaluateAsync(DiffAssertion assertion, string workingDirectory, CancellationToken cancellationToken)
    {
        foreach (var fileAssertion in assertion.FilesChanged)
        {
            var path = Path.Combine(workingDirectory, fileAssertion.Path);
            if (!File.Exists(path))
            {
                return $"assertions.diff: expected file '{fileAssertion.Path}' does not exist under the run's working directory";
            }

            if (fileAssertion.MustContain is { Length: > 0 } mustContain
                && !(await File.ReadAllTextAsync(path, cancellationToken)).Contains(mustContain, StringComparison.Ordinal))
            {
                return $"assertions.diff: '{fileAssertion.Path}' does not contain the expected text '{mustContain}'";
            }
        }

        return assertion.TestsAdded is true
            && !assertion.FilesChanged.Any(static file => file.Path.Contains("test", StringComparison.OrdinalIgnoreCase))
            ? "assertions.diff: testsAdded is true but no filesChanged entry names a test file "
                + "(heuristic check against the declared assertion set — not a full git diff over the working tree)"
            : null;
    }
}
