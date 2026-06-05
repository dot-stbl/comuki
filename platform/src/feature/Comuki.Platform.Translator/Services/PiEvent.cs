using System.Text.Json;

namespace Comuki.Platform.Translator.Services;

/// <summary>
/// Typed events emitted by <see cref="StreamJsonParser"/> as it consumes
/// <c>pi -p ... --output-format stream-json</c> output, one JSON object per line.
/// The shapes follow Claude Code's stream-json convention (Anthropic-compatible,
/// per architecture.md § 04 — the same convention <c>pi-coding-agent</c> uses).
/// </summary>
public abstract record PiEvent
{
    private PiEvent()
    {
    }

    /// <summary>
    /// Session init: model, tools, working dir. Always the first event in a run.
    /// </summary>
    public sealed record SystemEvent(string Subtype, string Cwd, IReadOnlyList<string> Tools) : PiEvent;

    /// <summary>
    /// User-prompt echo from the model. Useful for correlating what was actually sent.
    /// </summary>
    public sealed record UserEvent(string Content) : PiEvent;

    /// <summary>
    /// Text chunk from the assistant (streaming output).
    /// </summary>
    public sealed record AssistantTextEvent(string Text) : PiEvent;

    /// <summary>
    /// Tool invocation by the assistant (Bash, Read, Write, Edit, …).
    /// </summary>
    public sealed record AssistantToolUseEvent(string Tool, string InputJson) : PiEvent;

    /// <summary>
    /// Final session result — always the last event in a successful run.
    /// Carries the bottom-line status, duration, and (in production) cost.
    /// </summary>
    public sealed record ResultEvent(string Subtype, long DurationMs, decimal CostUsd, string Result) : PiEvent;

    /// <summary>
    /// Event type we don't model yet. The raw JSON is preserved so nothing is lost
    /// — we just don't surface it as a typed record.
    /// </summary>
    public sealed record UnknownEvent(string Type, JsonElement Raw) : PiEvent;

    /// <summary>
    /// A line that failed to parse as JSON. The parser does not throw on bad input;
    /// the orchestrator gets to decide whether to abort the run or just log it.
    /// </summary>
    public sealed record UnparseableEvent(string Line, string Error) : PiEvent;
}
