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
    /// pi-native session header — the first line of
    /// <c>pi --mode json</c> output (<c>{"type":"session",...}</c>).
    /// </summary>
    /// <param name="Version"></param>
    /// <param name="SessionId"></param>
    /// <param name="Cwd"></param>
    public sealed record SessionHeaderEvent(int Version, string SessionId, string Cwd) : PiEvent;

    /// <summary>
    /// pi-native streaming text delta from <c>message_update</c>
    /// (<c>assistantMessageEvent.type == "text_delta"</c>). Deltas are
    /// cumulative-assembly-only: concatenate <see cref="Delta"/> to build the
    /// live text.
    /// </summary>
    /// <param name="ContentIndex"></param>
    /// <param name="Delta"></param>
    public sealed record TextDeltaEvent(int ContentIndex, string Delta) : PiEvent;

    /// <summary>
    /// pi-native tool invocation — either a <c>toolcall_start</c> assistant
    /// message event (tool arguments being produced) or a
    /// <c>tool_execution_start</c> (the tool actually running).
    /// </summary>
    /// <param name="ToolName"></param>
    /// <param name="ArgsJson"></param>
    public sealed record ToolCallEvent(string ToolName, string ArgsJson) : PiEvent;

    /// <summary>
    /// pi-native end of the whole agent run (<c>agent_end</c>): the session
    /// finished and the final messages are available. Marks stream
    /// completion for the translator.
    /// </summary>
    public sealed record AgentEndEvent : PiEvent;

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
