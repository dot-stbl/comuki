using Comuki.AgentTest.Runner.Scenarios;

namespace Comuki.AgentEval.Corpus;

/// <summary>
/// One corpus entry: the base <see cref="ScenarioDefinition"/> loaded from
/// a <c>.scenario.yaml</c> or <c>.scenario.json</c> file under
/// <c>tests/fixtures/scenarios/agent-eval/</c>, the companion
/// <see cref="EvalExtension"/> parsed from the entry's <c>eval:</c>
/// top-level key, and the absolute path to the scenario file the entry
/// was loaded from.
/// </summary>
/// <param name="Scenario">The base scenario (worker.image, ticket, model, etc.) — reused as-is from <c>Comuki.AgentTest.Runner</c>.</param>
/// <param name="Eval">The companion eval-block; defaults to an empty <see cref="EvalExtension"/> when the block was absent on disk.</param>
/// <param name="ScenarioPath">Absolute path to the scenario file the entry was loaded from. Set by <see cref="CorpusLoader.LoadDirectory"/>.</param>
public sealed record CorpusEntry(ScenarioDefinition Scenario, EvalExtension Eval, string ScenarioPath);
