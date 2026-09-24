namespace Comuki.AgentEval.Corpus;

/// <summary>
/// The <c>eval:</c> top-level extension a WS10 corpus entry adds on top
/// of its base <c>ScenarioDefinition</c> shape. Every field is
/// optional/defaultable; a corpus entry that omits the entire
/// <c>eval:</c> block is valid — downstream judges report
/// not-applicable for any missing field rather than failing the run.
/// </summary>
/// <remarks>
/// Init-only properties (no positional primary constructor) so
/// YamlDotNet's <c>DefaultObjectFactory</c> can materialize this type
/// via its parameterless constructor. Records with a positional
/// primary constructor trigger <c>MissingMethodException</c> on
/// deserialization — same constraint as the wrapper.
/// </remarks>
public sealed class EvalExtension
{
    /// <summary>Free-text difficulty label — trivial/easy/medium/hard or whatever the corpus wants. Surfaced in the markdown report's per-entry row.</summary>
    public string Difficulty { get; init; } = string.Empty;

    /// <summary>Free-text expected outcome description; embedded in the LLM judge's user prompt when a rubric is declared.</summary>
    public string ExpectedOutcome { get; init; } = string.Empty;

    /// <summary>Repo-relative paths the agent is allowed to touch. Null or empty means the "files within allowed set" judge reports not-applicable.</summary>
    public List<string>? AllowedFiles { get; init; }

    /// <summary>Optional executable-and-args command the "tests pass" judge spawns inside the copied working directory. Null or empty means that judge reports not-applicable.</summary>
    public List<string>? TestCommand { get; init; }

    /// <summary>Optional ceiling on the total tool-call count observed in the run. Null means the "tool call count" judge reports not-applicable.</summary>
    public int? MaxToolCalls { get; init; }

    /// <summary>Optional LLM-as-judge rubric. Null means the entry is judged deterministically only.</summary>
    public EvalRubric? Rubric { get; init; }

    /// <summary>
    /// Resolved <see cref="AllowedFiles"/> — null collapses to an empty
    /// list so callers never have to null-check. Used by the
    /// "files within allowed set" judge and by the markdown report
    /// renderer.
    /// </summary>
    public IReadOnlyList<string> ResolvedAllowedFiles => AllowedFiles ?? [];

    /// <summary>
    /// Resolved <see cref="TestCommand"/> — null collapses to an empty
    /// list. Used by the "tests pass" judge.
    /// </summary>
    public IReadOnlyList<string> ResolvedTestCommand => TestCommand ?? [];
}
