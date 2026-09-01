namespace Comuki.Modules.Chat.Application.Graph;

/// <summary>
/// Channel names of the chat graph state. Every channel is LastValue and
/// carries wire-safe scalars (strings) only — the EF checkpointer round-trips
/// numbers as <c>long</c>, so string channels keep in-memory and Postgres
/// checkpointer behaviour identical.
/// </summary>
public static class ChatChannels
{
    /// <summary>The raw user message of the current turn.</summary>
    public const string UserMessage = "user_message";

    /// <summary>The task handed to the brain (slash-expanded, or the raw message).</summary>
    public const string Task = "task";

    /// <summary>Routing decision of the router node.</summary>
    public const string Phase = "phase";

    /// <summary>Brain invocation mode: <c>plan</c> or <c>chat</c>.</summary>
    public const string BrainKind = "brain_kind";

    /// <summary>Assistant reply text of the turn.</summary>
    public const string Reply = "reply";

    /// <summary>Memory digest fed to the brain this turn (journaled as a system message).</summary>
    public const string Digest = "digest";

    /// <summary>Validated plan JSON awaiting approval.</summary>
    public const string PlanJson = "plan_json";

    /// <summary>Run id the act node queued.</summary>
    public const string RunId = "run_id";

    /// <summary>Session id (string form) the nodes journal against.</summary>
    public const string SessionId = "session_id";

    /// <summary>Acting subject id (string form) — digest scope.</summary>
    public const string SubjectId = "subject_id";

    /// <summary>Project scope id (string form; empty when the session has none).</summary>
    public const string ProjectId = "project_id";

    /// <summary>Wizard state: <c>init</c> while the onboarding wizard is active, <c>done</c> after.</summary>
    public const string Wizard = "wizard";

    /// <summary>Current wizard step (string form of a 0-based index).</summary>
    public const string InitStep = "init_step";

    /// <summary>Wizard answers JSON.</summary>
    public const string InitAnswersJson = "init_answers_json";

    /// <summary>Tool name of the last tool observation.</summary>
    public const string ToolName = "tool_name";

    /// <summary>Tool result JSON of the last tool observation.</summary>
    public const string ToolResult = "tool_result";
}
