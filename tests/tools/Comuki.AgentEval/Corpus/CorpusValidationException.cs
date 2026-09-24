namespace Comuki.AgentEval.Corpus;

/// <summary>
/// Thrown by <see cref="CorpusLoader.LoadDirectory"/> when a corpus
/// directory's contents cannot be turned into a list of
/// <see cref="CorpusEntry"/> — a missing scenario file, a YAML parse
/// error, or a base <c>ScenarioDefinition</c> that fails
/// <c>ScenarioLoader.Validate</c>. Missing <c>eval:</c> blocks are NOT
/// errors; they silently default to <see cref="EvalExtension"/> with all
/// fields empty.
/// </summary>
public sealed class CorpusValidationException(string message, Exception? inner = null) : Exception(message, inner);
