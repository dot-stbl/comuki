namespace Comuki.Shared.Contracts.Chat;

/// <summary>
/// Stable wire keys of the <see cref="MessagePart"/> discriminator (the
/// <c>kind</c> property). The console switches on these strings, so they
/// are part of the contract: add a key, never rename one.
/// </summary>
public static class MessagePartKinds
{
    /// <summary>Markdown prose.</summary>
    public const string Text = "text";

    /// <summary>A source listing, optionally anchored at a file and line.</summary>
    public const string Code = "code";

    /// <summary>A diagram source in some dialect (mermaid, dot, …).</summary>
    public const string Diagram = "diagram";

    /// <summary>The model's visible reasoning for this turn.</summary>
    public const string Thinking = "thinking";

    /// <summary>One tool invocation with its arguments, status and observation.</summary>
    public const string Tool = "tool";

    /// <summary>A hand-off to a screen, carried as the search query that opens it.</summary>
    public const string Handoff = "handoff";

    /// <summary>The decomposition DAG awaiting (or recording) an approve decision.</summary>
    public const string Plan = "plan";
}
