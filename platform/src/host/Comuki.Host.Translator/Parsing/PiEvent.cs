using System.Text.Json;

namespace Comuki.Host.Translator.Parsing;

/// <summary>
/// Typed events emitted by <see cref="StreamJsonParser"/> as it consumes
/// <c>pi -p ... --output-format stream-json</c> output, one JSON object per line.
/// The shapes follow Claude Code's stream-json convention (Anthropic-compatible)
/// — the same convention <c>pi-coding-agent</c> uses.
/// </summary>
public abstract record PiEvent
{
    private PiEvent()
    {
    }

    /// <summary>Session init: model, tools, working dir. Always the first event in a run.</summary>
    /// <param name="Subtype"></param>
    /// <param name="Cwd"></param>
    /// <param name="Tools"></param>
    public sealed record SystemEvent(string Subtype, string Cwd, IReadOnlyList<string> Tools) : PiEvent;

    /// <summary>User-prompt echo from the model. Useful for correlating what was actually sent.</summary>
    /// <param name="Content"></param>
    public sealed record UserEvent(string Content) : PiEvent;

    /// <summary>Text chunk from the assistant (streaming output).</summary>
    /// <param name="Text"></param>
    public sealed record AssistantTextEvent(string Text) : PiEvent;

    /// <summary>Tool invocation by the assistant (Bash, Read, Write, Edit, …).</summary>
    /// <param name="Tool"></param>
    /// <param name="InputJson"></param>
    public sealed record AssistantToolUseEvent(string Tool, string InputJson) : PiEvent;

    /// <summary>
    /// Final session result — always the last event in a successful run.
    /// Carries the bottom-line status, duration and cost.
    /// </summary>
    /// <param name="Subtype"></param>
    /// <param name="DurationMs"></param>
    /// <param name="CostUsd"></param>
    /// <param name="Result"></param>
    public sealed record ResultEvent(string Subtype, long DurationMs, decimal CostUsd, string Result) : PiEvent;

    /// <summary>
    /// Event type we don't model yet. The raw JSON is preserved so nothing is
    /// lost — we just don't surface it as a typed record.
    /// </summary>
    /// <param name="Type"></param>
    /// <param name="Raw"></param>
    public sealed record UnknownEvent(string Type, JsonElement Raw) : PiEvent;

    /// <summary>
    /// A line that failed to parse as JSON. The parser does not throw on bad
    /// input; the orchestrator decides whether to abort the run or just log it.
    /// </summary>
    /// <param name="Line"></param>
    /// <param name="Error"></param>
    public sealed record UnparseableEvent(string Line, string Error) : PiEvent;
}
